using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProtoBuf
{

    public interface IAsyncSerializer<T>
    {
        ValueTask<T> DeserializeAsync(AsyncProtoReader reader, T value);
    }
    public static class SerializerExtensions
    {
        public static ValueTask<T> DeserializeAsync<T>(this IAsyncSerializer<T> serializer, Buffer<byte> buffer, bool useNewTextEncoder, T value = default(T))
        {
            async ValueTask<T> AwaitAndDispose(AsyncProtoReader reader, ValueTask<T> task)
            {
                using (reader) { return await task; }
            }
            {
                AsyncProtoReader reader = null;
                try
                {
                    reader = AsyncProtoReader.Create(buffer, useNewTextEncoder);
                    var task = serializer.DeserializeAsync(reader, value);
                    if (!task.IsCompleted)
                    {
                        var awaited = AwaitAndDispose(reader, task);
                        reader = null; // avoid disposal
                    }
                    return new ValueTask<T>(task.Result);
                }
                finally
                {
                    if (reader != null) reader.Dispose();
                }
            }
        }
    }
    public sealed class CustomSerializer : IAsyncSerializer<Customer>, IAsyncSerializer<Order>
    {
        private CustomSerializer() { }
        public static CustomSerializer Instance = new CustomSerializer();
        async ValueTask<Customer> IAsyncSerializer<Customer>.DeserializeAsync(AsyncProtoReader reader, Customer value)
        {
            var id = value?.Id ?? 0;
            var name = value?.Name ?? null;
            var notes = value?.Notes ?? null;
            var marketValue = value?.MarketValue ?? 0.0;
            var orders = value?.Orders ?? null;
            SubObjectToken tok = default(SubObjectToken);
            while (await reader.ReadNextFieldAsync())
            {
                switch(reader.FieldNumber)
                {
                    case 1:
                        id = await reader.ReadInt32Async();
                        break;
                    case 2:
                        name = await reader.ReadStringAsync();
                        break;
                    case 3:
                        notes = await reader.ReadStringAsync();
                        break;
                    case 4:
                        marketValue = await reader.ReadDoubleAsync();
                        break;
                    case 5:
                        if(orders == null)
                        {
                            if (value == null) value = new Customer();
                            orders = value.Orders;
                        }
                        do
                        {
                            tok = await reader.BeginSubObjectAsync();
                            orders.Add(await ((IAsyncSerializer<Order>)this).DeserializeAsync(reader, null));
                            reader.EndSubObject(ref tok);
                        } while (await reader.AssertNextField(5));
                        break;
                    default:
                        await reader.SkipFieldAsync();
                        break;
                }
            }
            if (value == null) value = new Customer();
            value.Id = id;
            value.Name = name;
            value.Notes = notes;
            value.MarketValue = marketValue;
            // no Orders setter
            return value;
        }
        async ValueTask<Order> IAsyncSerializer<Order>.DeserializeAsync(AsyncProtoReader reader, Order value)
        {
            int id = 0;
            string productCode = null;
            int quantity = 0;
            double unitPrice = 0.0;
            string notes = null;

            while (await reader.ReadNextFieldAsync())
            {
                switch (reader.FieldNumber)
                {
                    case 1:
                        id = await reader.ReadInt32Async();
                        break;
                    case 2:
                        productCode = await reader.ReadStringAsync();
                        break;
                    case 3:
                        quantity = await reader.ReadInt32Async();
                        break;
                    case 4:
                        unitPrice = await reader.ReadDoubleAsync();
                        break;
                    case 5:
                        notes = await reader.ReadStringAsync();
                        break;
                    default:
                        await reader.SkipFieldAsync();
                        break;
                }
            }
            if (value == null) value = new Order();
            value.Id = id;
            value.ProductCode = productCode;
            value.Quantity = quantity;
            value.UnitPrice = unitPrice;
            value.Notes = notes;
            return value;
        }
    }

    [ProtoContract]
    public class Customer
    {
        public override int GetHashCode()
        {
            int hash = -42;
            hash = (hash * -123124) + Id.GetHashCode();
            hash = (hash * -123124) + Name.GetHashCode();
            hash = (hash * -123124) + Notes.GetHashCode();
            hash = (hash * -123124) + MarketValue.GetHashCode();
            hash = (hash * -123124) + Orders.Count.GetHashCode();
            foreach(var order in Orders)
            {
                hash = (hash * -123124) + order.GetHashCode();
            }
            return hash;
        }

        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public string Name { get; set; }
        [ProtoMember(3)]
        public string Notes { get; set; }

        [ProtoMember(4)]
        public double MarketValue { get; set; }

        [ProtoMember(5)]
        public List<Order> Orders { get; } = new List<Order>();
    }
    [ProtoContract]
    public class Order
    {
        public override int GetHashCode()
        {
            int hash = -42;
            hash = (hash * -123124) + Id.GetHashCode();
            hash = (hash * -123124) + ProductCode.GetHashCode();
            hash = (hash * -123124) + Quantity.GetHashCode();
            hash = (hash * -123124) + UnitPrice.GetHashCode();
            hash = (hash * -123124) + Notes.GetHashCode();
            return hash;
        }
        [ProtoMember(1)]
        public int Id { get; set; }
        [ProtoMember(2)]
        public string ProductCode { get; set; }
        [ProtoMember(3)]
        public int Quantity { get; set; }
        [ProtoMember(4)]
        public double UnitPrice { get; set; }
        [ProtoMember(5)]
        public string Notes { get; set; }
    }
}
