using System;
using System.Collections.Generic;

namespace ProtoBuf{
	public class PType
	{
		static PType m_Current;
	    static PType Current
	    {
	        get
	        { 
				if (m_Current == null) {
					m_Current = new PType ();
				}
	            return m_Current;
	        }
	    }
		Dictionary<string, Type> m_Types = new Dictionary<string, Type>();
		
	    private PType() { }

	    void RegisterTypeInternal(string metaName, Type type)
	    {
			if (!m_Types.ContainsKey(metaName))
	        {
				m_Types.Add(metaName,type);
	        }
	        else
				throw new SystemException(string.Format("PropertyMeta : {0} is registered!",metaName));
	    }

		Type FindTypeInternal(string metaName)
		{
			Type type = null;
			if (!m_Types.TryGetValue(metaName, out type))
			{
				throw new SystemException(string.Format("PropertyMeta : {0} is not registered!", metaName));
			}
			return type;
		}

		public static void RegisterType(string metaName, Type type)
	    {
			Current.RegisterTypeInternal(metaName, type);
	    }

		static void RegisterDomain(ILRuntime.Runtime.Enviorment.AppDomain domain){
			appDomain = domain;
		}

		public static Type FindType(string metaName)
		{
			return Current.FindTypeInternal(metaName);
		}

		static ILRuntime.Runtime.Enviorment.AppDomain appDomain;
		public static object CreateInstance(Type type){
			if (Type.GetType (type.FullName) == null) {
				return appDomain.Instantiate (type.FullName);
			}
			return Activator.CreateInstance (type
				#if !(CF || SILVERLIGHT || WINRT || PORTABLE || NETSTANDARD1_3 || NETSTANDARD1_4)
				, nonPublic: true
				#endif
			);
		}
	}

	public static class __PTypeHelper{
		public static Type GetPType(this object o){
			var type = o.GetType ();
			if (type.FullName == "ILRuntime.Runtime.Intepreter.ILTypeInstance") {
				var ins = o as ILRuntime.Runtime.Intepreter.ILTypeInstance;
				type = PType.FindType (ins.Type.FullName);
			}
			return type;
		}
	}
}
