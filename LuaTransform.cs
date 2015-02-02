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
            yield return new RemoveCompilerGenerateField();
            yield return new AddPropertyImpl();
            yield return new MakeIndexerToMember();
            yield return new MakePropertyToMember();
            yield return new RemoveLicenseMethod();
            yield return new MakePublicFieldToMethod();
            yield return new RenameTransform();
        }

        #region help class

        #endregion
        
        #region rename
        // must be run after all properties and indexers convert to method
        class RenameTransform : ILuaTransform
        {
            public void Run(TNamespace ns)
            {

            }
        }
        #endregion

        #region AddPropertyImpl
        class AddPropertyImpl : ILuaTransform
        {
            public void Run(TNamespace ns)
            {

            }
        }
        #endregion

        #region RemoveCompilerGenerateField
        class RemoveCompilerGenerateField : ILuaTransform
        {
            public void Run(TNamespace ns)
            {
                foreach (var td in ns.classes)
                {
                    var c = td as TClass;
                    if (c != null)
                    {
                        c.fields.RemoveAll(item => (item.attributes!=null) && (item.attributes.Contains("CompilerGenerated")));
                    }
                }
            }
        }
        #endregion
        #region MakePublicFieldToMethod
        class MakePublicFieldToMethod : ILuaTransform
        {
            public void Run(TNamespace ns)
            {

            }
        }
        #endregion
        #region RemoveLicenseMethod
        class RemoveLicenseMethod : ILuaTransform
        {
            public void Run(TNamespace ns)
            {
                foreach (var td in ns.classes)
                {
                    var c = td as TClass;
                    if (c != null)
                    {
                        c.constructors.RemoveAll(item => item.body.text.Contains("LicenseManager"));
                    }
                }
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
                                m.body.text = p.getter.body.text;
                                m.body.invokes.Clear();
                                m.body.invokes.AddRange(p.getter.body.invokes);
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
                                m.body.text = p.setter.body.text;
                                m.body.invokes.Clear();
                                m.body.invokes.AddRange(p.getter.body.invokes);
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
