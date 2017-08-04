# protobuf-net
protobuf-net is a contract based serializer for .NET code, that happens to write data in the "protocol buffers" serialization format engineered by Google. The API, however, is very different to Google's, and follows typical .NET patterns (it is broadly comparable, in usage, to XmlSerializer, DataContractSerializer, etc). It should work for most .NET languages that write standard types and can use attributes.

## Release Notes

[Change history and pending changes are here](http://mgravell.github.io/protobuf-net/releasenotes)

## Donate

If you feel like supporting my efforts, I won't stop you:

<a href='https://pledgie.com/campaigns/33946'><img alt='Click here to lend your support to: protobuf-net; fast binary serialization for .NET and make a donation at pledgie.com !' src='https://pledgie.com/campaigns/33946.png?skin_name=chrome' border='0' ></a>

If you can't, that's fine too.

---

Supported Runtimes :
- .NET Framework 4.0+
- .NET Standard 1.3+

Legacy Runtimes (up to v2.1.0)
- .NET Framework 2.0/3.0/3.5
- Compact Framework 2.0/3.5
- Mono 2.x
- Silverlight, Windows Phone 7&8
- Windows 8 apps

## install

Nuget : `Install-Package protobuf-net`

## Basic usage

### 1 First Decorate your classes
```csharp
[ProtoContract]
class Person {
    [ProtoMember(1)]
    public int Id {get;set;}
    [ProtoMember(2)]
    public string Name {get;set;}
    [ProtoMember(3)]
    public Address Address {get;set;}
}
[ProtoContract]
class Address {
    [ProtoMember(1)]
    public string Line1 {get;set;}
    [ProtoMember(2)]
    public string Line2 {get;set;}
}
```
Note that unlike XmlSerializer, the member-names are not encoded in the data - instead, you must pick an integer to identify each member. Additionally, to show intent it is necessary to show that we intend this type to be serialized (i.e. that it is a data contract).

### 2 Serialize your data

This writes a 32 byte file to "person.bin" :
```csharp
var person = new Person {
    Id = 12345, Name = "Fred",
    Address = new Address {
        Line1 = "Flat 1",
        Line2 = "The Meadows"
    }
};
using (var file = File.Create("person.bin")) {
    Serializer.Serialize(file, person);
}
```

### 3 Deserialize your data

This reads the data back from "person.bin" :
```csharp
Person newPerson;
using (var file = File.OpenRead("person.bin")) {
    newPerson = Serializer.Deserialize<Person>(file);
}
```

### Notes 

#### Notes for Identifiers

* they must be positive integers 
* they must be unique within a single type but the same numbers can be re-used in sub-types if inheritance is enabled 
* the identifiers must not conflict with any inheritance identifiers (discussed later) 
* lower numbers take less space - don't start 100,000,000 
* the identifier is important; you can change the member-name, or shift it between a property and a field, but changing the identifier changes the data 

#### Notes on types

supported: 
* custom classes that: 
  * are marked as data-contract 
  * have a parameterless constructor 
  * for Silverlight: are public 
* many common primitives etc 
* single dimension arrays: T[] 
* List<T> / IList<T> 
* Dictionary<TKey,TValue> / IDictionary<TKey,TValue> 
* any type which implements IEnumerable<T> and has an Add(T) method 

The code assumes that types will be mutable around the elected members. Accordingly, custom structs are not supported, since they should be immutable. 

## Advanced subjects

### Inheritance

Inheritance must be explicitly declared, in a similar way that if must for XmlSerializer and DataContractSerializer. This is done via [ProtoInclude(...)] on each type with known sub-types: 

```csharp
[ProtoContract]
[ProtoInclude(7, typeof(SomeDerivedType))]
class SomeBaseType {...}

[ProtoContract]
class SomeDerivedType {...}
```
There is no special significance in the 7 above; it is an integer key, just like every [ProtoMember(...)]. It must be unique in terms of SomeBaseType (no other [ProtoInclude(...)] or [ProtoMember(...)] in SomeBaseType can use 7), but does not need to be unique globally. 

### .proto file

As an alternative to writing your classes and decorating them, You can generate your types and serializer from a .proto schema. 

This done using the precompiler. [Additional guidance can be found here](http://blog.marcgravell.com/2012/07/introducing-protobuf-net-precompiler.html).

### Alternative to attributes

In v2, everything that can be done with attributes can also be configured at runtime via RuntimeTypeModel. The Serializer.* methods are basically just shortcuts to RuntimeTypeModel.Default.*, so to manipulate the behaviour of Serializer.*, you must configure RuntimeTypeModel.Default. 




支持ILRuntime
Unity中使用 需要注册一下 
		static bool InitedILRuntime = false;
		static IMethod s_HFInitialize;
		static IMethod s_HFUpdate;
		static ILRuntime.Runtime.Enviorment.AppDomain HFDomain;
		static void InitializeILRuntime(){
			BundleHelper.LoadAsset("assets.domain.hotfixed.bytes.ab",(wd) => {
				BundleHelper.LoadAsset("assets.domain.hotfixep.bytes.ab",(wp)=>{
					var bytesed = (wd.asset as TextAsset).bytes;
					var bytesep = wp.asset == null ? null : (wp.asset as TextAsset).bytes;
					var msb = new MemoryStream(bytesed);
					var msp = new MemoryStream(bytesep);
					HFDomain = new ILRuntime.Runtime.Enviorment.AppDomain ();
					#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
					msp = null;
					#endif
					HFDomain.LoadAssembly(msb, msp, new PdbReaderProvider());
					InitializeILRuntimeCLR();
					Initialize();
					InitedILRuntime = true;
				});
			});
		}
	    static void InitializeILRuntimeCLR(){

			//ILRuntime.Runtime.Generated.CLRBindings.Initialize(HFDomain);
			ProtoBuf.PType.RegisterFunctionCreateInstance(PType_CreateInstance);
			ProtoBuf.PType.RegisterFunctionGetRealType(PType_GetRealType);

	    }
		static void Initialize(){
			var hfMain = HFDomain.GetType ("HotFix.Main");
			s_HFInitialize = hfMain.GetMethod ("Initialize", 0);
			s_HFUpdate = hfMain.GetMethod ("Update", 0);
			HFDomain.Invoke (s_HFInitialize, null, null);
		}
		public static void Update(){
			if (InitedILRuntime) {
				HFDomain.Invoke (s_HFUpdate, null, null);
			}
		}

		static object PType_CreateInstance(string typeName){
			return HFDomain.Instantiate (typeName);
		}
		static Type PType_GetRealType(object o){
			var type = o.GetType ();
			if (type.FullName == "ILRuntime.Runtime.Intepreter.ILTypeInstance") {
				var ilo = o as ILRuntime.Runtime.Intepreter.ILTypeInstance;
				type = ProtoBuf.PType.FindType (ilo.Type.FullName);
			}
			return type;
		}

Dll 中使用 参考 hotfix目录下main.cs

        public static void Initialize()
        {
            ILRuntime_mmopb.Initlize();
            Debug.Log("Initialize");
        }
        
	public static void Update()
	{
            if (!s_Initialized) return;
            var c = new mmopb.m_login_c();
            c.account = new mmopb.p_account_c();
            c.account.account = "abc";
            c.account.snapshots.Add(new mmopb.p_avatar_snapshot());
            c.account.snapshots.Add(new mmopb.p_avatar_snapshot());
			var s = new mmopb.p_avatar_snapshot();
			s.avatar = new mmopb.p_entity_basis();
			s.avatar.account = "defxxx";
            c.account.snapshots.Add(s);
			c.account.snapshots.Add(s);
            var stream = new System.IO.MemoryStream();
            ProtoBuf.Serializer.Serialize(stream,c);
            Debug.Log(stream.Length);
            var bytes = stream.ToArray();
            var t = ProtoBuf.Serializer.Deserialize(typeof(mmopb.m_login_c), new System.IO.MemoryStream(bytes)) as mmopb.m_login_c;
            Debug.Log(t.account.snapshots.Count);
            Debug.Log("Update"+t.account.snapshots[3].avatar.account);
	}



