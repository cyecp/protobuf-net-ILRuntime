using ProtoBuf.Meta;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace ProtoBuf.Issues
{
    public class IdempotentTypeModel
    {
        [Fact]
        public void CctorTimingImpact()
        {
            using (var ms = new MemoryStream())
            {
                var model = TypeModel.Create(); // so we don't break the other test
                model[typeof(ProtoList<string>)][1].SupportNull = true;
                model.Serialize(ms, new ProtoList<string> { List = new[] { "abc", "def" } });

                ms.Position = 0;
                var clone = Serializer.Deserialize<EvilProtoList<string>>(ms);
                Assert.Equal("abc,def", string.Join(",", clone.List));
            }
        }

        [ProtoContract]
        public class EvilProtoList<T>
        {
            [ProtoMember(1, IsRequired = false, Name = @"List", DataFormat = ProtoBuf.DataFormat.Default)]
            [System.ComponentModel.DefaultValue(null)]
            public IList<T> List { get; set; }

            static EvilProtoList()
            {
                RuntimeTypeModel.Default[typeof(EvilProtoList<T>)][1].SupportNull = true;
            }
        }

        [Fact]
        public void ChangingSupportNullToSameValueWorks()
        {
            // can change any number of times before we serialize
            RuntimeTypeModel.Default[typeof(ProtoList<string>)][1].SupportNull = true;
            RuntimeTypeModel.Default[typeof(ProtoList<string>)][1].SupportNull = false;
            RuntimeTypeModel.Default[typeof(ProtoList<string>)][1].SupportNull = true;
            RuntimeTypeModel.Default[typeof(ProtoList<string>)][1].SupportNull = false;
            RuntimeTypeModel.Default[typeof(ProtoList<string>)][1].SupportNull = true;

            // do the first serialization here (model becomes frozen)
            Assert.Equal("abc,def",
                string.Join(",", Serializer.DeepClone(
                    new ProtoList<string> { List = new[] { "abc", "def" } }
                ).List));


            // it should not throw if we "change" it to the same value,
            // even after serialization
            RuntimeTypeModel.Default[typeof(ProtoList<string>)][1].SupportNull = true;

            Assert.Equal("ghi,jkl",
                string.Join(",", Serializer.DeepClone(
                    new ProtoList<string> { List = new[] { "ghi", "jkl" } }
                ).List));

            // and again just for luck
            RuntimeTypeModel.Default[typeof(ProtoList<string>)][1].SupportNull = true;
            
            // but it *should* throw if a different value is used after serialization
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                RuntimeTypeModel.Default[typeof(ProtoList<string>)][1].SupportNull = false;
            });
            Assert.Equal(
                "The type cannot be changed once a serializer has been generated",
                ex.Message);
        }
        
        [ProtoContract]
        public class ProtoList<T>
        {
            [ProtoMember(1, IsRequired = false, Name = @"List", DataFormat = ProtoBuf.DataFormat.Default)]
            [System.ComponentModel.DefaultValue(null)]
            public IList<T> List { get; set; }
        }

    }
}