using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QuantKit
{
    public interface ILuaTransform
    {
        void Run(TNamespace ns);
    }

    class LuaTransform : ILuaTransform
    {
     public void Run(TNamespace ns)
        {
            foreach (var t in GetTransforms())
            {
                t.Run(ns);
            }
        }

        IEnumerable<ILuaTransform> GetTransforms()
        {
            yield return new MakeIndexerToMember();
            yield return new MakePropertyToMember();
            yield return new RenameTransform();
        }

        #region help class

        #endregion
        
        #region rename
        class RenameTransform : ILuaTransform
        {
            public void Run(TNamespace ns)
            {

            }
        }
    #endregion


    #region MakeIndexerToMethod
        class MakeIndexerToMember : ILuaTransform
        {
            public void Run(TNamespace ns)
            {
                foreach (var item in ns.classes)
                {
                    TClass c = item as TClass;
                    if (c != null)
                    {
                        foreach (var p in c.indexers)
                        {
                            if (p.getter != null)
                            {
                                TMethod m = new TMethod();
                                m.name = "get" + p.name;
                                if (p.getter.isInternal || p.getter.isPrivate)
                                    m.isPrivate = true;
                                else
                                    m.isPublic = true;
                                m.type = p.type;
                                m.parameters.AddRange(p.parameters);
                                m.body = p.getter.body;
                                c.methods.Add(m);
                            }

                            if (p.setter != null)
                            {
                                TMethod m = new TMethod();
                                m.name = "set" + p.name;
                                if (p.setter.isInternal || p.setter.isPrivate)
                                    m.isPrivate = true;
                                else
                                    m.isPublic = true;
                                m.type = "void";
                                m.parameters.AddRange(p.parameters);
                                m.body = p.setter.body;
                                c.methods.Add(m);
                            }
                        }
                    }
                    c.indexers.Clear();
                }
            }
        }
    #endregion

    #region MakePropertyToMember
        class MakePropertyToMember : ILuaTransform
        {
            public void Run(TNamespace ns)
            {
                foreach (var item in ns.classes)
                {
                    TClass c = item as TClass;
                    if (c != null)
                    {
                        foreach (var p in c.properties)
                        {
                            if (p.getter != null)
                            {
                                TMethod m = new TMethod();
                                m.name = "get" + p.name;
                                if (p.getter.isInternal || p.getter.isPrivate)
                                    m.isPrivate = true;
                                else
                                    m.isPublic = true;
                                m.type = p.type;
                                m.body = p.getter.body;
                                c.methods.Add(m);
                            }

                            if (p.setter != null)
                            {
                                TMethod m = new TMethod();
                                m.name = "set" + p.name;
                                if (p.setter.isInternal || p.setter.isPrivate)
                                    m.isPrivate = true;
                                else
                                    m.isPublic = true;
                                m.type = "void";
                                TParameter param = new TParameter();
                                param.type = p.type;
                                param.name = "value";
                                m.parameters.Add(param);
                                m.body = p.setter.body;
                                c.methods.Add(m);
                            }
                        }
                    }
                    c.properties.Clear();
                }
            }
        }
    #endregion
    }
}
