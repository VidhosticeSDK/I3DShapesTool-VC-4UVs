using System.IO;
using Xunit;
using I3DShapesTool.Lib.Model;
using System.Linq;
using I3DShapesTool.Lib.Tools;
using System.Collections.Generic;

namespace I3DShapesToolTest
{
    public class UnitTest
    {
        private const float MaxUV = 10f;

        public UnitTest()
        {
        }

        private static void AssertShapesFile(ShapesFile file, byte seed, short version, int shapeCount)
        {
            Assert.Equal(seed, file.Seed);
            Assert.Equal(version, file.Version);
            Assert.Equal(shapeCount, file.Parts.Length);
        }

        private static void AssertShape(I3DShape shape, string name, uint shapeId, int vertexCount, int faceCount)
        {
            Assert.Equal(name, shape.Name);
            Assert.Equal(shapeId, shape.Id);
            Assert.Equal(vertexCount, shape.Positions.Length);
            Assert.Equal(faceCount, shape.Triangles.Length);

            if (shape.Normals != null)
            {
                Assert.Equal(vertexCount, shape.Normals.Length);
            }

            foreach (var uvSet in shape.UVSets)
            {
                if (uvSet != null)
                {
                    Assert.Equal(vertexCount, uvSet.Length);
                }
            }
        }

        private static void AssertShapeData(ShapesFile file)
        {
            foreach (var shape in file.Shapes)
            {
                // This UV check works in 99.9% percent of cases but some models just have extremely wacky UVs which means we can't rely on this test :(
                //Assert.True(shape.UVSets.All(uvSet => uvSet == null || uvSet.All(uv => uv.U >= -MaxUV && uv.U <= MaxUV && uv.V >= -MaxUV && uv.V <= MaxUV)));

                Assert.True(shape.Triangles.All(tri => tri.P1Idx <= shape.CornerCount && tri.P2Idx <= shape.CornerCount && tri.P3Idx <= shape.CornerCount));
                if (shape.Normals != null)
                {
                    double numUnitLengths = shape.Normals.Sum(v => v.IsValidNormal() ? 1 : 0);
                    // The data files can contain some bad normals, but most of them should be good
                    Assert.True(numUnitLengths / shape.Normals.Length > 0.95);
                    Assert.True(shape.Normals.First().IsValidNormal());
                    Assert.True(shape.Normals.Last().IsValidNormal());
                }
            }
        }

        private static void TestRewrite(ShapesFile file)
        {
            foreach (var part in file.Parts)
            {
                var originalRaw = part.RawData;

                using var ms = new MemoryStream();
                using var bw = new EndianBinaryWriter(ms, part.Endian);
                part.Write(bw);
                bw.Flush();

                var newRaw = ms.ToArray();
                /*
                //Useful for debugging but a bit slow so leaving uncommented
                for(var i = 0; i < originalRaw.Length; i++)
                {
                    Assert.Equal(originalRaw[i], newRaw[i]);
                }
                */
                Assert.Equal(originalRaw.Length, newRaw.Length);
                Assert.Equal(originalRaw, newRaw);
            }
        }

        private static void FindShapesFiles(string baseDir, ISet<string> outData)
        {
            foreach(var file in Directory.GetFiles(baseDir))
            {
                if (file.EndsWith(".i3d.shapes"))
                {
                    outData.Add(file);
                }
            }

            foreach(var dir in Directory.GetDirectories(baseDir))
            {
                FindShapesFiles(dir, outData);
            }
        }

        [SkippableFact]
        public void TestFS22WriteShapes()
        {
            var gameFolder = SteamHelper.GetGameDirectoryOrSkip("Farming Simulator 22");
            var filePath = Path.Combine(gameFolder, @"data\vehicles\boeckmann\bigMasterWesternWCF\bigMasterWesternWCF.i3d.shapes");

            using var fileStream = File.OpenRead(filePath);
            var file = new ShapesFile();
            file.Load(fileStream);

            using var ms = new MemoryStream();
            file.Write(ms);
            var rewrittenData = ms.ToArray();

            var originalData = File.ReadAllBytes(filePath);
            Assert.Equal(originalData.Length, rewrittenData.Length);
            Assert.Equal(originalData, rewrittenData);

            ms.Seek(0, SeekOrigin.Begin);

            var file2 = new ShapesFile();
            file2.Load(ms);

            Assert.Equal(file.Seed, file2.Seed);
            Assert.Equal(file.Version, file2.Version);
            Assert.Equal(file.Parts.Length, file2.Parts.Length);
            Assert.Equal(file.Parts[0].RawData, file2.Parts[0].RawData);
        }

        [SkippableFact]
        public void TestFS22()
        {
            var gameFolder = SteamHelper.GetGameDirectoryOrSkip("Farming Simulator 22");

            {
                using var fileStream = File.OpenRead(Path.Combine(gameFolder, @"data\vehicles\boeckmann\bigMasterWesternWCF\bigMasterWesternWCF.i3d.shapes"));
                var file = new ShapesFile();
                file.Load(fileStream);
                AssertShapesFile(file, 153, 7, 24);
                AssertShape(file.Shapes.First(), "alphaShape", 20, 368, 260);
                AssertShapeData(file);
                TestRewrite(file);
            }
            {
                using var fileStream = File.OpenRead(Path.Combine(gameFolder, @"data\vehicles\newHolland\chSeries\chSeries.i3d.shapes"));
                var file = new ShapesFile();
                file.Load(fileStream);
                AssertShapesFile(file, 142, 7, 192);
                AssertShape(file.Shapes.First(), "airFilterCleanerShape", 116, 1381, 1020);
                AssertShapeData(file);
                TestRewrite(file);
            }
        }

        [SkippableFact]
        public void TestFS22Full()
        {
            var gameFolder = SteamHelper.GetGameDirectoryOrSkip("Farming Simulator 22");

            var shapeFiles = new HashSet<string>();
            FindShapesFiles(Path.Combine(gameFolder, "data"), shapeFiles);

            foreach(var filePath in shapeFiles)
            {
                using var fileStream = File.OpenRead(filePath);
                var file = new ShapesFile();
                file.Load(fileStream, null, true);
                AssertShapeData(file);
            }
        }

        [SkippableFact]
        public void TestFS19()
        {
            var gameFolder = SteamHelper.GetGameDirectoryOrSkip("Farming Simulator 19");

            using var fileStream = File.OpenRead(Path.Combine(gameFolder, @"data\vehicles\magsi\telehandlerBaleFork\telehandlerBaleFork.i3d.shapes"));
            var file = new ShapesFile();
            file.Load(fileStream);
            AssertShapesFile(file, 201, 5, 9);
            AssertShape(file.Shapes.First(), "colPartBackShape1", 4, 24, 12);
            AssertShapeData(file);
            TestRewrite(file);
        }

        [SkippableFact]
        public void TestFS19Full()
        {
            var gameFolder = SteamHelper.GetGameDirectoryOrSkip("Farming Simulator 19");

            var shapeFiles = new HashSet<string>();
            FindShapesFiles(Path.Combine(gameFolder, "data"), shapeFiles);

            foreach (var filePath in shapeFiles)
            {
                using var fileStream = File.OpenRead(filePath);
                var file = new ShapesFile();
                file.Load(fileStream, null, true);
                AssertShapeData(file);
            }
        }

        [SkippableFact]
        public void TestFS17()
        {
            var gameFolder = SteamHelper.GetGameDirectoryOrSkip("Farming Simulator 17");

            using var fileStream = File.OpenRead(Path.Combine(gameFolder, @"data\vehicles\tools\magsi\wheelLoaderLogFork.i3d.shapes"));
            var file = new ShapesFile();
            file.Load(fileStream);
            AssertShapesFile(file, 49, 5, 12);
            AssertShape(file.Shapes.First(), "wheelLoaderLogForkShape", 1, 24, 12);
            AssertShapeData(file);
            TestRewrite(file);
        }

        [SkippableFact]
        public void TestFS17Full()
        {
            var gameFolder = SteamHelper.GetGameDirectoryOrSkip("Farming Simulator 17");

            var shapeFiles = new HashSet<string>();
            FindShapesFiles(Path.Combine(gameFolder, "data"), shapeFiles);

            foreach (var filePath in shapeFiles)
            {
                using var fileStream = File.OpenRead(filePath);
                var file = new ShapesFile();
                file.Load(fileStream, null, true);
                AssertShapeData(file);
            }
        }

        [SkippableFact]
        public void TestFS15()
        {
            var gameFolder = SteamHelper.GetGameDirectoryOrSkip("Farming Simulator 15");

            using var fileStream = File.OpenRead(Path.Combine(gameFolder, @"data\vehicles\tools\grimme\grimmeFT300.i3d.shapes"));
            var file = new ShapesFile();
            file.Load(fileStream);
            AssertShapesFile(file, 188, 3, 16);
            AssertShape(file.Shapes.First(), "grimmeFTShape300", 1, 40, 20);
            AssertShapeData(file);
            TestRewrite(file);
        }

        [SkippableFact]
        public void TestFS15Full()
        {
            var gameFolder = SteamHelper.GetGameDirectoryOrSkip("Farming Simulator 15");

            var shapeFiles = new HashSet<string>();
            FindShapesFiles(Path.Combine(gameFolder, "data"), shapeFiles);

            foreach (var filePath in shapeFiles)
            {
                using var fileStream = File.OpenRead(filePath);
                var file = new ShapesFile();
                file.Load(fileStream, null, true);
                AssertShapeData(file);
            }
        }

        [SkippableFact]
        public void TestFS13()
        {
            var gameFolder = SteamHelper.GetGameDirectoryOrSkip("Farming Simulator 2013");

            using var fileStream = File.OpenRead(Path.Combine(gameFolder, @"data\vehicles\tools\kuhn\kuhnGA4521GM.i3d.shapes"));
            var file = new ShapesFile();
            file.Load(fileStream);
            AssertShapesFile(file, 68, 2, 32);
            AssertShape(file.Shapes.First(), "blanketBarShape2", 26, 68, 44);
            AssertShapeData(file);
            TestRewrite(file);
        }

        [SkippableFact]
        public void TestFS13Full()
        {
            var gameFolder = SteamHelper.GetGameDirectoryOrSkip("Farming Simulator 2013");

            var shapeFiles = new HashSet<string>();
            FindShapesFiles(Path.Combine(gameFolder, "data"), shapeFiles);

            foreach (var filePath in shapeFiles)
            {
                using var fileStream = File.OpenRead(filePath);
                var file = new ShapesFile();
                file.Load(fileStream, null, true);
                AssertShapeData(file);
            }
        }
    }
}
