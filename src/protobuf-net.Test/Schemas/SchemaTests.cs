﻿using Google.Protobuf.Reflection;
using Newtonsoft.Json;
using ProtoBuf.Reflection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace ProtoBuf.Schemas
{
    [Trait("kind", "schema")]
    public class SchemaTests
    {
        private ITestOutputHelper _output;

        const string SchemaPath = "Schemas";
        public static IEnumerable<object[]> GetSchemas()
            => from file in Directory.GetFiles(SchemaPath, "*.proto", SearchOption.AllDirectories)
               select new object[] { Regex.Replace(file.Replace('\\', '/'), "^Schemas/", "") };

        [Fact]
        public void CanWriteMessageSetData()
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new ProtoWriter(ms, null, null))
                {
                    ProtoWriter.WriteFieldHeader(5, WireType.String, writer);
                    var tok = ProtoWriter.StartSubItem(null, writer);

                    ProtoWriter.WriteFieldHeader(1, WireType.StartGroup, writer);
                    var tok2 = ProtoWriter.StartSubItem(null, writer);

                    ProtoWriter.WriteFieldHeader(2, WireType.Variant, writer);
                    ProtoWriter.WriteInt32(15447542, writer);

                    ProtoWriter.WriteFieldHeader(3, WireType.String, writer);
                    var tok3 = ProtoWriter.StartSubItem(null, writer);

                    ProtoWriter.WriteFieldHeader(1, WireType.String, writer);
                    ProtoWriter.WriteString("EmbeddedMessageSetElement", writer);

                    ProtoWriter.EndSubItem(tok3, writer);
                    ProtoWriter.EndSubItem(tok2, writer);

                    ProtoWriter.EndSubItem(tok, writer);
                }

                var hex = BitConverter.ToString(ms.ToArray(), 0, (int)ms.Length);
                Assert.Equal("2A-24-0B-10-F6-EB-AE-07-1A-1B-0A-19"
                           + "-45-6D-62-65-64-64-65-64-4D-65-73-73-61-67-65-53"
                           + "-65-74-45-6C-65-6D-65-6E-74-0C", hex);
            }
        }

        [Fact]
        public void CanRountripExtensionData()
        {
            var obj = new CanRountripExtensionData_WithFields { X = 1, Y = 2};
            using (var ms = new MemoryStream())
            {
                Serializer.Serialize(ms, obj);
                var a = BitConverter.ToString(ms.ToArray());
                ms.Position = 0;
                var raw = Serializer.Deserialize<CanRountripExtensionData_WithoutFields>(ms);
                ms.Position = 0;
                ms.SetLength(0);
                Serializer.Serialize(ms, raw);
                var b = BitConverter.ToString(ms.ToArray());

                Assert.Equal(a, b);

                var extData = raw.ExtensionData;
                Assert.NotEqual(0, extData?.Length ?? 0);

                extData = raw.ExtensionData;
                Assert.NotEqual(0, extData?.Length ?? 0);
            }
        }
        [ProtoContract]
        class CanRountripExtensionData_WithFields
        {
            [ProtoMember(1)]
            public int X { get; set; }
            [ProtoMember(2)]
            public int Y { get; set; }
        }
        [ProtoContract]
        class CanRountripExtensionData_WithoutFields : Extensible
        {
            public byte[] ExtensionData
            {
                get { return DescriptorProto.GetExtensionData(this); }
                set { DescriptorProto.SetExtensionData(this, value); }
            }
        }

        [Fact]
        public void BasicCompileClientWorks()
        {
            var result = CSharpCompiler.Compile(new CodeFile("my.proto", "syntax=\"proto3\"; message Foo {}"));
            Assert.Equal(0, result.Errors.Length);
            Assert.True(result.Files.Single().Text.StartsWith("// This file was generated by a tool;"));
        }

        static JsonSerializerSettings jsonSettings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };


        [Theory]
        [MemberData(nameof(GetSchemas))]
        public void CompareProtoToParser(string path)
        {
            var schemaPath = Path.Combine(Directory.GetCurrentDirectory(), SchemaPath);
            _output.WriteLine(Path.GetDirectoryName(
                Path.Combine(schemaPath, path).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)));

            var protocBinPath = Path.Combine(schemaPath, Path.ChangeExtension(path, "protoc.bin"));
            int exitCode;
            using (var proc = new Process())
            {
                var psi = proc.StartInfo;
                psi.FileName = "protoc";
                psi.Arguments = $"--descriptor_set_out={protocBinPath} {path}";
                psi.RedirectStandardError = psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.WorkingDirectory = schemaPath;
                proc.Start();
                var stdout = proc.StandardOutput.ReadToEndAsync();
                var stderr = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(5000))
                {
                    try { proc.Kill(); } catch { }
                }
                exitCode = proc.ExitCode;
                string err = "", @out = "";
                if (stdout.Wait(1000)) @out = stdout.Result;
                if (stderr.Wait(1000)) err = stderr.Result;

                if (!string.IsNullOrWhiteSpace(@out))
                {
                    _output.WriteLine("stdout: ");
                    _output.WriteLine(@out);
                }
                if (!string.IsNullOrWhiteSpace(err))
                {
                    _output.WriteLine("stderr: ");
                    _output.WriteLine(err);
                }
            }
            FileDescriptorSet set;
            string protocJson = null, jsonPath;
            if (exitCode == 0)
            {
                using (var file = File.OpenRead(protocBinPath))
                {
                    set = Serializer.Deserialize<FileDescriptorSet>(file);
                    protocJson = JsonConvert.SerializeObject(set, Formatting.Indented, jsonSettings);
                    jsonPath = Path.Combine(schemaPath, Path.ChangeExtension(path, "protoc.json"));
                    File.WriteAllText(jsonPath, protocJson);
                }
            }



            set = new FileDescriptorSet();

            set.AddImportPath(schemaPath);
            bool isProto3 = set.Add(path, includeInOutput: true) && set.Files[0].Syntax == "proto3";
            if (isProto3)
            {
                using (var proc = new Process())
                {
                    var psi = proc.StartInfo;
                    psi.FileName = "protoc";
                    psi.Arguments = $"--csharp_out={Path.GetDirectoryName(protocBinPath)} {path}";
                    psi.RedirectStandardError = psi.RedirectStandardOutput = true;
                    psi.UseShellExecute = false;
                    psi.WorkingDirectory = schemaPath;
                    proc.Start();
                    var stdout = proc.StandardOutput.ReadToEndAsync();
                    var stderr = proc.StandardError.ReadToEndAsync();
                    if (!proc.WaitForExit(5000))
                    {
                        try { proc.Kill(); } catch { }
                    }
                    exitCode = proc.ExitCode;
                    string err = "", @out = "";
                    if (stdout.Wait(1000)) @out = stdout.Result;
                    if (stderr.Wait(1000)) err = stderr.Result;

                    if (!string.IsNullOrWhiteSpace(@out))
                    {
                        _output.WriteLine("stdout (C#): ");
                        _output.WriteLine(@out);
                    }
                    if (!string.IsNullOrWhiteSpace(err))
                    {
                        _output.WriteLine("stderr (C#): ");
                        _output.WriteLine(err);
                    }
                    _output.WriteLine("exit code(C#): " + exitCode);
                }
            }

            set.Process();

            var parserBinPath = Path.Combine(schemaPath, Path.ChangeExtension(path, "parser.bin"));
            using (var file = File.Create(parserBinPath))
            {
                set.Serialize(file, false);
            }

            var parserJson = set.Serialize((s, o) => JsonConvert.SerializeObject(s, Formatting.Indented, jsonSettings), false);

            var errors = set.GetErrors();
            Exception genError = null;

            try
            {
                foreach (var file in CSharpCodeGenerator.Default.Generate(set))
                {
                    var newExtension = "parser" + Path.GetExtension(file.Name);
                    var newFileName = Path.ChangeExtension(file.Name, newExtension);
                    File.WriteAllText(Path.Combine(schemaPath, newFileName), file.Text);
                }
            }
            catch (Exception ex)
            {
                genError = ex;
                _output.WriteLine(ex.Message);
                _output.WriteLine(ex.StackTrace);
            }



            jsonPath = Path.Combine(schemaPath, Path.ChangeExtension(path, "parser.json"));
            File.WriteAllText(jsonPath, parserJson);


            if (errors.Any())
            {
                _output.WriteLine("Parser errors:");
                foreach (var err in errors) _output.WriteLine(err.ToString());
            }

            _output.WriteLine("Protoc exited with code " + exitCode);

            var errorCount = errors.Count(x => x.IsError);
            if (exitCode == 0)
            {
                Assert.Equal(0, errorCount);
            }
            else
            {
                Assert.NotEqual(0, errorCount);
            }



            var parserBytes = File.ReadAllBytes(parserBinPath);
            using (var ms = new MemoryStream(parserBytes))
            {
                var selfLoad = Serializer.Deserialize<FileDescriptorSet>(ms);
                var selfLoadJson = JsonConvert.SerializeObject(selfLoad, Formatting.Indented, jsonSettings);
                // should still be the same! 
                Assert.Equal(parserJson, selfLoadJson);
            }
            var parserHex = GetPrettyHex(parserBytes);
            File.WriteAllText(Path.ChangeExtension(parserBinPath, "parser.hex"), parserHex);

            if (exitCode == 0)
            {
                var protocHex = GetPrettyHex(File.ReadAllBytes(protocBinPath));
                File.WriteAllText(Path.ChangeExtension(protocBinPath, "protoc.hex"), protocHex);

                switch(path)
                {
                    case "google/protobuf/unittest_custom_options.proto":
                        // this is a special case; the two encoders choose slightly different
                        // layouts for the same data; both are valid; I'm happy that this is OK
                        // - this was why the "decode" tool (on the website) was written!
                        break;
                    default:
                        // compare results
                        Assert.Equal(protocJson, parserJson);
                        Assert.Equal(protocHex, parserHex);
                        break;
                }
                
            }



            Assert.Null(genError);
        }

        public SchemaTests(ITestOutputHelper output) => _output = output;

        public static string GetPrettyHex(byte[] bytes)
        {
            var sb = new StringBuilder();
            int left = bytes.Length, offset = 0;
            while(left > 0)
            {
                int take = Math.Min(left, 16);
                sb.AppendLine(BitConverter.ToString(bytes, offset, take));
                left -= take;
                offset += take;
            }
            return sb.ToString();
        }

    }
}
