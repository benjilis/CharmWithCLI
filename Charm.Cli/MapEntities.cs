using Arithmic;
using Tiger;
using Tiger.Exporters;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Tiger.Schema.Activity.DESTINY1_RISE_OF_IRON;
using Tiger.Schema.Activity.DESTINY2_SHADOWKEEP_2601;

namespace Charm.Cli;

// Copied from Charm/Activity/ActivityMapEntityView.xaml.cs (ExportFull / ExtractDataTables), WPF-free.
internal static class MapEntities
{
    public static void ExportFull(List<FileHash> dataTables, string hash, string savePath)
    {
        Directory.CreateDirectory(savePath);
        ExtractDataTables(dataTables, hash, savePath);
    }

    private static void ExtractDataTables(List<FileHash> dataTables, string hash, string savePath)
    {
        GlobalExporterScene globalScene = Exporter.Get().GetOrCreateGlobalScene();

        // todo these scenes can be combined
        ExporterScene entitiesScene = Exporter.Get().CreateScene($"{hash}_Entities", ExportType.Entities, DataExportType.Map);
        ExporterScene skyScene = Exporter.Get().CreateScene($"{hash}_SkyObjects", ExportType.SkyObjects, DataExportType.Map);
        ExporterScene decoratorsScene = Exporter.Get().CreateScene($"{hash}_Decorators", ExportType.Decorators, DataExportType.Map);
        ExporterScene treesScene = Exporter.Get().CreateScene($"{hash}_SpeedTrees", ExportType.SpeedTrees, DataExportType.Map);
        ExporterScene roadDecalsScene = Exporter.Get().CreateScene($"{hash}_RoadDecals", ExportType.RoadDecals, DataExportType.Map);
        ExporterScene waterDecalsScene = Exporter.Get().CreateScene($"{hash}_WaterDecals", ExportType.WaterDecals, DataExportType.Map);

        FileOrder.ParallelForEach(dataTables, data =>
        {
            if (Strategy.IsD1() && data.GetReferenceHash().Hash32 == 0x808003F6)
            {
                List<SMapDataEntry> dataEntries = FileResourcer.Get().GetSchemaTag<S808003F6>(data).TagData.EntityComponent.CollapseIntoDataEntry();
                foreach (SMapDataEntry entry in dataEntries)
                {
                    Entity entity = FileResourcer.Get().GetFile<Entity>(entry.Entity.Hash);
                    if (entity.HasGeometry())
                    {
                        entitiesScene.AddMapEntity(entry);
                        entity.SaveMaterialsFromParts(entitiesScene, entity.Load(ExportDetailLevel.MostDetailed));
                    }
                    //else
                    //    dynamicPointScene.AddEntityPoints(entry);
                }
            }
            else
            {
                Tag<SMapDataTable> dataTable = FileResourcer.Get().GetSchemaTag<SMapDataTable>(data);
                dataTable.TagData.DataEntries.ForEach(entry =>
                {
                    Entity entity = FileResourcer.Get().GetFile<Entity>(entry.Entity.Hash);
                    if (entity.HasGeometry())
                    {
                        entitiesScene.AddMapEntity(entry);
                        entity.SaveMaterialsFromParts(entitiesScene, entity.Load(ExportDetailLevel.MostDetailed));
                    }
                    else
                    {
                        foreach (FileHash? resourceHash in entity.Components)
                        {
                            EntityComponent resource = FileResourcer.Get().GetFile<EntityComponent>(resourceHash);
                            switch (resource.TagData.Unk10.GetValue(resource.GetReader()))
                            {
                                case S80809479:
                                    var a = ((S80808179)resource.TagData.Unk18.GetValue(resource.GetReader()));
                                    DynamicArray<S808091F1> b = a.Array1;
                                    b.AddRange(a.Array2);

                                    foreach (S808091F1 c in b)
                                    {
                                        if (c.Unk10.GetValue(resource.GetReader()) is S808091D1)
                                        {
                                            // I dont like adding the whole resource here, but its the only way to get the data out of it
                                            globalScene.AddToGlobalScene(resource);
                                            break;
                                        }
                                    }
                                    break;
                            }
                        }

                        //if (entry.Translation.ToVec3() == Tiger.Schema.Vector3.Zero)
                        //    System.Console.WriteLine($"World origin resource {dataTable.Hash} Resource? {entry.DataResource.GetValue(dataTable.GetReader())}");
                        //dynamicPointScene.AddEntityPoints(entry);
                    }

                    switch (entry.DataResource.GetValue(dataTable.GetReader()))
                    {
                        case SMapSkyObjectsResource skyResource:
                            skyResource.SkyObjects?.Load();
                            if (skyResource.SkyObjects is not null)
                                skyResource.SkyObjects.LoadIntoExporter(skyScene);
                            break;

                        case SMapCubemapResource cubemapResource:
                            Cubemap cubemap = new(cubemapResource);
                            cubemap.CubemapTransform = entry.Transfrom;
                            cubemap.LoadIntoExporter();
                            break;

                        case SMapLightResource mapLight:
                            mapLight.Lights?.Load();
                            if (mapLight.Lights is not null)
                                mapLight.Lights.LoadIntoExporter();
                            break;

                        case SExpensiveLightResource shadowingLight:
                            shadowingLight.ShadowingLight?.Load();
                            if (shadowingLight.ShadowingLight is not null)
                            {
                                shadowingLight.ShadowingLight.Transfrom = entry.Transfrom;
                                shadowingLight.ShadowingLight.LoadIntoExporter();
                            }
                            break;

                        case SMapDecalsResource decals:
                            decals.MapDecals?.Load();
                            if (decals.MapDecals is not null)
                                decals.MapDecals.LoadIntoExporter(entitiesScene);
                            break;

                        case SDecoratorMapResource decorator:
                            decorator.Decorator?.Load();
                            if (decorator.Decorator is not null)
                                decorator.Decorator.LoadIntoExporter(decoratorsScene, treesScene, savePath);
                            break;

                        case SMapWaterDecal waterDecal:
                            if (waterDecal.Model is null)
                                return;

                            WaterDecals water = new(waterDecal);
                            water.Transform = entry.Transfrom;
                            water.LoadIntoExporter(waterDecalsScene);
                            break;

                        case SMapAtmosphere atmosphere:
                            if (!globalScene.Any<SMapAtmosphere>())
                                globalScene.AddToGlobalScene(atmosphere, true);
                            break;

                        case SMapLensFlareResource lensFlare:
                            if (lensFlare.LensFlare is not null)
                            {
                                lensFlare.LensFlare.Transform = entry.Transfrom;
                                lensFlare.LensFlare.LoadIntoExporter(entitiesScene);
                            }
                            break;

                        case SMapRoadDecalsResource roadDecals:
                            roadDecals.RoadDecals?.Load();
                            if (roadDecals.RoadDecals is null)
                                return;

                            roadDecals.RoadDecals.LoadIntoExporter(roadDecalsScene);
                            break;

                        case S80806A71 dayCycle:
                            if (!globalScene.Any<S80806A71>())
                                globalScene.AddToGlobalScene(dayCycle, true);
                            break;

                        default:
                            break;
                    }
                });
            }
        });
    }
}
