﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using CommandLine.Text;
using I3DShapesTool.Configuration;
using I3DShapesTool.Lib.Container;
using I3DShapesTool.Lib.Export;
using I3DShapesTool.Lib.Model;
using I3DShapesTool.Lib.Model.I3D;
using I3DShapesTool.Lib.Tools;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Layouts;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using LogLevel = NLog.LogLevel;

namespace I3DShapesTool
{
    class Program
    {
        public static readonly ILoggerProvider LoggerProvider = new NLog.Extensions.Logging.NLogLoggerProvider();
        public static readonly ILogger Logger = LoggerProvider.CreateLogger("all");

        private static void Main(string[] args)
        {
            SetupLogging();

            try
            {
                ParserResult<CommandLineOptions> result = Parser.Default.ParseArguments<CommandLineOptions>(args);
                result
                    .WithParsed(Run)
                    .WithNotParsed(errs => DisplayHelp(result, errs));
            }
            catch(ArgumentValidationException e)
            {
                Logger.LogError(e.Message);
                Console.Read();
            }
            finally
            {
                LogManager.Shutdown();
            }
        }

        private static void SetupLogging(CommandLineOptions options = null)
        {
            NLog.Config.LoggingConfiguration config = new NLog.Config.LoggingConfiguration();

            NLog.Targets.ConsoleTarget logconsole = new NLog.Targets.ConsoleTarget("logConsole")
            {
                Layout = new SimpleLayout("[${level:uppercase=true}] ${message}")
            };

            LogLevel minLevel = LogLevel.Info;
            if(options != null)
            {
                minLevel = options.Verbose ? LogLevel.Debug : LogLevel.Info;
                if(options.Quiet)
                    minLevel = LogLevel.Error;
            }

            config.AddRule(minLevel, LogLevel.Fatal, logconsole);

            LogManager.Configuration = config;

            Lib.Tools.Logger.Instance = Logger;
        }

        /// <summary>
        /// Tests if the shapes loaded from this file are valid shapes
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        private static bool IsFileLoadSuccessful(ShapesFile file)
        {
            // All shape names should contain only valid ASCII characters, if they don't we know something has gone wrong
            return file.Shapes.SelectMany(shape => shape.Name).All(c => c >= 0x20 && c <= 0x7E);
        }

        private static ShapesFile LoadFileBruteForce(string filePath)
        {
            using FileStream fileStream = File.OpenRead(filePath);

            ShapesFile file = new ShapesFile();
            bool success = false;

            byte seed;
            for(seed = 0; seed < 0xFF; seed++)
            {
                try
                {
                    file.Load(fileStream, seed);
                }
                catch(DecryptFailureException)
                {
                    continue;
                }

                if(!IsFileLoadSuccessful(file))
                    continue;

                success = true;
                break;
            }

            if(!success)
            {
                Logger.LogWarning("Failed to find any matching seed for this file.");
                return null;
            }

            Logger.LogInformation($"Found successful seed {seed}");
            return file;
        }

        private static ShapesFile LoadFile(string filePath)
        {
            using FileStream fileStream = File.OpenRead(filePath);

            ShapesFile file = new ShapesFile();

            Logger.LogInformation($"Loading file: {Path.GetFileName(filePath)}");
            try
            {
                file.Load(fileStream);
            }
            catch(DecryptFailureException)
            {
                Logger.LogInformation("Failed decrypting file. Attempting to brute-force the seed...");
                return LoadFileBruteForce(filePath);
            }

            return file;
        }

        private static string GetTargetFolder(CommandLineOptions options, string folderName)
        {
            string folder;
            if(options.CreateDir)
            {
                folder = Path.Combine(options.Out, "extract_" + folderName);
                Directory.CreateDirectory(folder);
            }
            else
            {
                folder = options.Out;
            }
            return folder;
        }

        private static void DumpBinary(ShapesFile file, string outFolder)
        {
            foreach(I3DPart part in file.Parts)
            {
                string binFileName = $"{PartBinaryFilePrefix(part)}_{part.Name}_{part.Id}.bin";
                string filePath = Path.Combine(outFolder, CleanFileName(binFileName));

                using FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                using BinaryWriter binaryWriter = new BinaryWriter(fileStream);
                part.Write(binaryWriter, (short)file.Version);
            }
        }

        private static void ExtractFile(I3D i3dFile, string outFolder, CommandLineOptions options)      // parsed i3d (xml) data
        {
            foreach(Shape shape in i3dFile.GetShapes())
            {
              //string mdlFileName = Path.Combine(outFolder, CleanFileName($"{shape.Name}_{shape.Id}.obj"));            // orig
                string mdlFileName = Path.Combine(outFolder, CleanFileName($"{shape.ShapeId:000}_{shape.Name}_{shape.Id}.objx"));  // shapeId + i3d_Name + i3d_Id

                I3DShape shapeData = shape.ShapeData;
                if(shapeData == null)
                    throw new ArgumentException("Shape doesn't have any assigned shape data");

                using FileStream fs = new FileStream(mdlFileName, FileMode.OpenOrCreate, FileAccess.Write);

                WavefrontObj objfile = new WavefrontObj(shapeData, i3dFile.Name, 1);
                if(options.Transform)
                    objfile.Transform(shape.AbsoluteTransform);
                objfile.Export(fs);
            }
        }

        private static void ExtractFile(ShapesFile file, string shapesFileName, string outFolder)       // only .shapes file
        {
            foreach(I3DShape shape in file.Shapes)
            {
              //string mdlFileName = Path.Combine(outFolder, CleanFileName($"{shape.Name}_{shape.Id}.obj"));            // orig
                string mdlFileName = Path.Combine(outFolder, CleanFileName($"{shape.Id:000}_{shape.Name}.objx"));       // shapeId + shape_name

                using FileStream fs = new FileStream(mdlFileName, FileMode.OpenOrCreate, FileAccess.Write);

                new WavefrontObj(shape, shapesFileName)
                    .Export(fs);
            }
            // export all splines to separate files
            int i = 1;
            foreach(Spline spline in file.Splines)
            {
                Logger.LogInformation($"--- SPLINE: {i} ---");

                string mdlFileName = Path.Combine(outFolder, CleanFileName($"spline{i}.i3d"));
                using FileStream fs = new FileStream(mdlFileName, FileMode.OpenOrCreate, FileAccess.Write);

                using(StreamWriter s = new InvariantStreamWriter(fs))
                {
                    s.WriteLine("<?xml version=\"1.0\" encoding=\"iso-8859-1\"?>\n"
                        +"<i3D name=\"3.i3d\" version=\"1.6\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"http://i3d.giants.ch/schema/i3d-1.6.xsd\">\n"
                        +"  <Asset><Export program=\"GIANTS Editor 64bit\" version=\"9.0.4\"/></Asset>\n"
                        +"  <Files></Files>\n"
                        +"  <Materials><Material name=\"UnnamedMaterial\" materialId=\"6\" diffuseColor=\"1 1 1 1\"></Material></Materials>\n"
                        +"  <Shapes>\n"
                        +"    <NurbsCurve name=\"splineGeometry\" shapeId=\"1\" type=\"cubic\" degree=\"3\" form=\"open\">");
                    foreach(I3DVector p in spline.Points)
                    {
                        s.WriteLine("      <cv c=\"{0:F6} {1:F6} {2:F6}\"/>", p.X, p.Y, p.Z);
                    }
                    s.WriteLine("    </NurbsCurve>\n"
                        +"  </Shapes>\n"
                        +"  <Dynamics></Dynamics>\n"
                        +"  <Scene><Shape shapeId=\"1\" name=\"spline\" nodeId=\"5\" distanceBlending=\"false\"/></Scene>\n"
                        +"</i3D>");
                }
                i += 1;
            }
        }

        private static void ProcessFileInput(CommandLineOptions options)
        {
            string i3dFile = null;
            if(options.File.EndsWith(".i3d.shapes"))
            {
                string i3dFileCandidate = options.File[0..^7];
                if(File.Exists(i3dFileCandidate))
                {
                    // Found .i3d file in same directory as the supplied .i3d.shapes
                    i3dFile = i3dFileCandidate;
                }
            }
            else if(options.File.EndsWith(".i3d"))
            {
                i3dFile = options.File;
            }
            else
            {
                throw new ArgumentValidationException($"Unrecognized file {options.File}.");
            }

            if(i3dFile != null)
            {
                Logger.LogInformation("File is I3D, parsing data from XML.");

                I3D i3d = I3DXMLReader.ParseXML(i3dFile);
                if(i3d.ExternalShapesFile == null || !File.Exists(i3d.ExternalShapesFile))
                {
                    Logger.LogInformation("No valid externalShapesFile specified in I3D, nothing to do.");
                    return;
                }

                ShapesFile file = LoadFile(i3d.ExternalShapesFile);
                i3d.LinkShapesFile(file);
                string folder = GetTargetFolder(options, Path.GetFileName(i3d.Name));
                if(options.DumpBinary)
                {
                    DumpBinary(file, folder);
                }
                ExtractFile(i3d, folder, options);
            }
            else
            {
                // i3dFile is null but shapesFile isn't
                Logger.LogInformation("Couldn't find matching I3D XML file, parsing only raw shapes data.");

                ShapesFile file = LoadFile(options.File);
                string folder = GetTargetFolder(options, Path.GetFileName(options.File));
                if(options.DumpBinary)
                {
                    DumpBinary(file, folder);
                }
                string shapesFileName = Path.GetFileName(options.File).Replace(".i3d.shapes", "");
                ExtractFile(file, shapesFileName, folder);
            }
        }

        private static string PartBinaryFilePrefix(I3DPart part)
        {
            return part.Type switch
            {
                EntityType.Shape => "shape",
                EntityType.Spline => "spline",
                _ => $"part",
            };
        }

        private static void Run(CommandLineOptions options)
        {
            SetupLogging(options); // Set it up again now that we have verbosity information

            if(!File.Exists(options.File))
                throw new ArgumentValidationException($"File {options.File} does not exist.");

            if(options.Out == null)
                options.Out = Path.GetDirectoryName(options.File);
            else if(!Directory.Exists(options.Out))
                throw new ArgumentValidationException($"Directory {options.Out} does not exist.");

            ProcessFileInput(options);

            Logger.LogInformation("Done");
        }

        private static void DisplayHelp<T>(ParserResult<T> result, IEnumerable<Error> errs)
        {
            HelpText helpText = HelpText.AutoBuild(result, h => HelpText.DefaultParsingErrorsHandler(result, h), e => e);

            foreach(string s in helpText.ToString().Split('\n'))
            {
                Logger.LogInformation(s);
            }
            foreach(Error e in errs)
            {
                Logger.LogError(e.Tag.ToString());
            }

            Console.Read();
        }

        /// <summary>
        /// https://stackoverflow.com/a/7393722/2911165
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private static string CleanFileName(string fileName)
        {
            return Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c.ToString(), string.Empty));
        }
    }

    class ArgumentValidationException : Exception
    {
        public ArgumentValidationException(string message) : base(message) { }
    }
}
