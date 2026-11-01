using System.Reflection;
using Newtonsoft.Json;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Activity;

namespace Charm.Cli;

internal static class Program
{
    private static int Main(string[] argv)
    {
        var opts = new Options(argv);
        if (opts.Command == null || opts.Has("help"))
        {
            Console.WriteLine(Usage);
            return opts.Command == null ? 1 : 0;
        }

        try
        {
            if (!Bootstrap.Init(opts))
                return 2;

            switch (opts.Command)
            {
                case "activities": return Commands.Activities(opts);
                case "bubbles": return Commands.Bubbles(opts);
                default:
                    Console.Error.WriteLine($"Unknown command '{opts.Command}'.");
                    Console.Error.WriteLine(Usage);
                    return 1;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Error: {e}");
            return 3;
        }
    }

    private const string Usage = @"charm-cli --game <packages dir> --strategy <TigerStrategy> [--out <dir>] <command> [args]

Commands:
  activities [filter]     list activities (hash, name); filter = case-insensitive substring
  bubbles <activity>      list the bubbles (zones) of an activity (hash or name substring)

Strategies: see the TigerStrategy enum (DESTINY1_RISE_OF_IRON, DESTINY2_LATEST, ...)";
}

internal class Options
{
    public string? Command;
    public List<string> Positional = new();
    private readonly Dictionary<string, string> _named = new(StringComparer.OrdinalIgnoreCase);

    public Options(string[] argv)
    {
        for (int i = 0; i < argv.Length; i++)
        {
            string a = argv[i];
            if (a.StartsWith("--"))
            {
                string key = a[2..];
                if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--"))
                    _named[key] = argv[++i];
                else
                    _named[key] = "true";
            }
            else if (Command == null)
                Command = a;
            else
                Positional.Add(a);
        }
    }

    public bool Has(string k) => _named.ContainsKey(k);
    public string? Get(string k) => _named.TryGetValue(k, out string? v) ? v : null;
}

internal static class Bootstrap
{
    public static bool Init(Options o)
    {
        string? game = o.Get("game");
        string? strategyName = o.Get("strategy");
        if (game == null || strategyName == null)
        {
            Console.Error.WriteLine("--game and --strategy are required.");
            return false;
        }
        if (!Enum.TryParse(strategyName, true, out TigerStrategy strategy))
        {
            Console.Error.WriteLine($"Unknown strategy '{strategyName}'. Known: {string.Join(", ", Enum.GetNames<TigerStrategy>())}");
            return false;
        }
        string outDir = Path.GetFullPath(o.Get("out") ?? "charm-out");
        Directory.CreateDirectory(outDir);

        // ConfigSubsystem reads a JSON config; build a private one next to the exe (never inside a game folder).
        string cfgPath = Path.Combine(AppContext.BaseDirectory, "charm-cli.config.json");
        var settings = new Settings
        {
            Common = new CommonSettings
            {
                CurrentStrategy = strategy,
                ExportPath = outDir,
                PackagesPath = new Dictionary<TigerStrategy, string> { [strategy] = Path.GetFullPath(game) },
            },
            Unreal = new UnrealSettings(),
            Source2 = new Source2Settings(),
        };
        File.WriteAllText(cfgPath, JsonConvert.SerializeObject(settings, Formatting.Indented));

        TigerInstance.Args = new TigerArgs(new[] { "charm-cli", "-config", cfgPath, "-strategy", strategy.ToString() });
        InitialiseStrategistSingletons();
        TigerInstance.InitialiseSubsystems();
        return true;
    }

    // Same registration as Charm/MainWindow.xaml.cs (InitialiseStrategistSingletons).
    private static void InitialiseStrategistSingletons()
    {
        var all = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).ToList();
        HashSet<Type> lazy = all
            .Select(t => t.GetNonGenericParent(typeof(Strategy.LazyStrategistSingleton<>)))
            .Where(t => t is { ContainsGenericParameters: false })
            .Select(t => t!.GetNonGenericParent(typeof(Strategy.StrategistSingleton<>))!)
            .ToHashSet();
        HashSet<Type> singletons = all
            .Select(t => t.GetNonGenericParent(typeof(Strategy.StrategistSingleton<>)))
            .Where(t => t is { ContainsGenericParameters: false })
            .Select(t => t!)
            .ToHashSet();
        singletons.ExceptWith(lazy);

        foreach (Type t in SortByInitializationOrder(singletons.ToList()))
            t.GetMethod("RegisterEvents", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
    }

    private static List<Type> SortByInitializationOrder(List<Type> types)
    {
        var dependencyMap = new Dictionary<Type, List<Type>>();
        var dependencyCount = new Dictionary<Type, int>();
        foreach (Type type in types)
        {
            foreach (InitializeAfterAttribute attribute in type.GenericTypeArguments[0].GetCustomAttributes(typeof(InitializeAfterAttribute), true))
            {
                Type dep = attribute.TypeToInitializeAfter.GetNonGenericParent(typeof(Strategy.StrategistSingleton<>))!;
                if (!dependencyMap.ContainsKey(dep))
                {
                    dependencyMap[dep] = new List<Type>();
                    dependencyCount[dep] = 0;
                }
                dependencyMap[dep].Add(type);
                dependencyCount[type] = dependencyCount.ContainsKey(type) ? dependencyCount[type] + 1 : 1;
            }
        }
        var sorted = types.Where(t => !dependencyCount.ContainsKey(t)).ToList();
        var queue = new Queue<Type>(dependencyMap.Keys.Where(k => dependencyCount[k] == 0));
        while (queue.Count > 0)
        {
            Type type = queue.Dequeue();
            sorted.Add(type);
            if (dependencyMap.TryGetValue(type, out var dependents))
                foreach (Type d in dependents)
                    if (--dependencyCount[d] == 0)
                        queue.Enqueue(d);
        }
        if (sorted.Count < types.Count)
            throw new InvalidOperationException("Circular dependency detected.");
        return sorted;
    }
}

internal static class Commands
{
    public static IEnumerable<FileHash> AllActivities()
    {
        if (Strategy.IsD1())
        {
            return PackageResourcer.Get().GetD1Activities()
                .Where(a => a.Value == 0x8080052E).Select(a => a.Key);
        }
        return PackageResourcer.Get().GetAllHashes<IActivity>();
    }

    public static int Activities(Options o)
    {
        string? filter = o.Positional.FirstOrDefault();
        var rows = AllActivities()
            .Select(h => (Hash: h, Name: PackageResourcer.Get().GetActivityName(h)))
            .Where(r => filter == null || r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || r.Hash.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Name, StringComparer.Ordinal);
        int n = 0;
        foreach (var r in rows)
        {
            Console.WriteLine($"{r.Hash}\t{r.Name}");
            n++;
        }
        Console.Error.WriteLine($"{n} activities");
        return 0;
    }

    public static FileHash? ResolveActivity(string key)
    {
        var all = AllActivities().ToList();
        var byHash = all.FirstOrDefault(h => h.ToString().Equals(key, StringComparison.OrdinalIgnoreCase));
        if (byHash != null) return byHash;
        var matches = all.Where(h => PackageResourcer.Get().GetActivityName(h).Contains(key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1) return matches[0];
        Console.Error.WriteLine(matches.Count == 0 ? $"No activity matches '{key}'." : $"{matches.Count} activities match '{key}', be more precise:");
        foreach (var m in matches.Take(30))
            Console.Error.WriteLine($"  {m}\t{PackageResourcer.Get().GetActivityName(m)}");
        return null;
    }

    public static int Bubbles(Options o)
    {
        if (o.Positional.Count == 0) { Console.Error.WriteLine("bubbles <activity hash|name>"); return 1; }
        var hash = ResolveActivity(o.Positional[0]);
        if (hash == null) return 1;
        IActivity activity = FileResourcer.Get().GetFileInterface<IActivity>(hash);
        Console.Error.WriteLine($"{hash}\t{PackageResourcer.Get().GetActivityName(hash)}\tdestination: {activity.DestinationName}");
        int n = 0;
        foreach (Bubble b in activity.EnumerateBubbles())
        {
            Console.WriteLine($"{b.ChildMapReference.Hash}\t{b.Name}");
            n++;
        }
        Console.Error.WriteLine($"{n} bubbles");
        return 0;
    }
}
