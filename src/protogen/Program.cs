﻿using Google.Protobuf.Reflection;
using ProtoBuf;
using ProtoBuf.Reflection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace protogen
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {

                string outPath = null; // -o{FILE}, --descriptor_set_out={FILE}
                bool version = false; // --version
                bool help = false; // -h, --help
                var importPaths = new List<string>(); // -I{PATH}, --proto_path={PATH}
                var inputFiles = new List<string>(); // {PROTO_FILES} (everything not `-`)
                bool exec = false;
                CodeGenerator codegen = null;
                foreach (string arg in args)
                {
                    string lhs = arg, rhs = "";
                    int index = arg.IndexOf('=');
                    if (index > 0)
                    {
                        lhs = arg.Substring(0, index);
                        rhs = arg.Substring(index + 1);
                    }
                    else if (arg.StartsWith("-o"))
                    {
                        lhs = "--descriptor_set_out";
                        rhs = arg.Substring(2);
                    }
                    else if (arg.StartsWith("-I"))
                    {
                        lhs = "--proto_path";
                        rhs = arg.Substring(2);
                    }

                    switch (lhs)
                    {
                        case "":
                            break;
                        case "--version":
                            version = true;
                            break;
                        case "-h":
                        case "--help":
                            help = true;
                            break;
                        case "--csharp_out":
                            outPath = rhs;
                            codegen = CSharpCodeGenerator.Default;
                            exec = true;
                            break;
                        case "--descriptor_set_out":
                            outPath = rhs;
                            codegen = null;
                            exec = true;
                            break;
                        case "--proto_path":
                            importPaths.Add(rhs);
                            break;
                        default:
                            if (lhs.StartsWith("-") || !string.IsNullOrWhiteSpace(rhs))
                            {
                                help = true;
                                break;
                            }
                            else
                            {
                                inputFiles.Add(lhs);
                            }
                            break;
                    }
                }

                if (help)
                {
                    ShowHelp();
                    return 0;
                }
                else if (version)
                {
                    Console.WriteLine($"protogen {GetVersion<Program>()}");
                    Console.WriteLine($"protobuf-net {GetVersion<ProtoReader>()}");
                    Console.WriteLine($"protobuf-net.Reflection {GetVersion<FileDescriptorSet>()}");
                    return 0;
                }
                else if (inputFiles.Count == 0)
                {
                    Console.Error.WriteLine("Missing input file.");
                    return -1;
                }
                else if (!exec)
                {
                    Console.Error.WriteLine("Missing output directives.");
                    return -1;
                }
                else
                {
                    int exitCode = 0;
                    var set = new FileDescriptorSet();
                    if (importPaths.Count == 0)
                    {
                        set.AddImportPath(Directory.GetCurrentDirectory());
                    }
                    else
                    {
                        foreach (var dir in importPaths)
                        {
                            if (Directory.Exists(dir))
                            {
                                set.AddImportPath(dir);
                            }
                            else
                            {
                                Console.Error.WriteLine($"Directory not found: {dir}");
                                exitCode = 1;
                            }
                        }
                    }
                    
                    foreach (var input in inputFiles)
                    {
                        if (!set.Add(input, true))
                        {
                            Console.Error.WriteLine($"File not found: {input}");
                            exitCode = 1;
                        }
                    }
                    if(exitCode != 0) return exitCode;

                    set.Process();
                    var errors = set.GetErrors();
                    foreach(var err in errors)
                    {
                        if (err.IsError) exitCode++;
                        Console.Error.WriteLine(err.ToString());
                    }
                    if (exitCode != 0) return exitCode;

                    if (codegen == null)
                    {
                        using (var fds = File.Create(outPath))
                        {
                            Serializer.Serialize(fds, set);
                        }
                    }
                    else
                    {
                        var files = codegen.Generate(set);
                        foreach (var file in files)
                        {
                            var path = Path.Combine(outPath, file.Name);
                            File.WriteAllText(path, file.Text);
                            Console.WriteLine($"generated: {path}");
                        }
                    }
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return -1;
            }
        }
        static string GetVersion<T>()
        {
#if NETCOREAPP1_3
            var attrib = typeof(T).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
#else
            var attribs = typeof(T).Assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
            var attrib = attribs.Length == 0 ? null : attribs[0] as AssemblyInformationalVersionAttribute;
#endif
            return attrib?.InformationalVersion ?? "(unknown)";
        }

        private static void ShowHelp()
        { // deliberately mimicking "protoc"'s calling syntax
            Console.WriteLine(@"Usage: protogen [OPTION] PROTO_FILES
Parse PROTO_FILES and generate output based on the options given:
  -IPATH, --proto_path=PATH   Specify the directory in which to search for
                              imports.  May be specified multiple times;
                              directories will be searched in order.  If not
                              given, the current working directory is used.
  --version                   Show version info and exit.
  -h, --help                  Show this text and exit.
  -oFILE,                     Writes a FileDescriptorSet (a protocol buffer,
    --descriptor_set_out=FILE defined in descriptor.proto) containing all of
                              the input files to FILE.
  --csharp_out=OUT_DIR        Generate C# source file.");
        }
    }
}
