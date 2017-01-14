﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using ChakraHost.Hosting;
using System.Linq.Expressions;
using System.Reflection;

namespace ChakraSharp.Port
{
    public class TypeWrapper
    {
        static Dictionary<Type, TypeWrapper> cache = new Dictionary<Type, TypeWrapper>();
        public static void ClearCache()
        {
            cache = new Dictionary<Type, TypeWrapper>();
        }

        public static TypeWrapper Wrap(Type type)
        {
            if (cache.ContainsKey(type))
                return cache[type];
            var tw = new TypeWrapper(type);
            cache[type] = tw;
            return tw;
        }

        public JavaScriptValue GetJavaScriptValue()
        {
            return constructorValue;
        }

        Type type;
        public JavaScriptValue constructorValue;
        public JavaScriptValue prototypeValue;
        TypeWrapper(Type type)
        {
            TypeWrapper baseTypeWrapper = null;
            if (type.BaseType != null)
            {
                baseTypeWrapper = TypeWrapper.Wrap(type.BaseType);
            }

            this.type = type;
            var ctors = type.GetConstructors();
            if (ctors.Length == 0)
            {
                constructorValue = JavaScriptValue.CreateFunction(NoConstructor, GCHandle.ToIntPtr(GCHandle.Alloc(this)));
            }
            else if (ctors.Length == 1)
            {
                var ctorw = new ConstructorWrapper(ctors[0]);
                constructorValue = ctorw.GetJavaScriptValue();
            }
            else
            {
                var os = new OverloadSelector();
                foreach (var m in ctors)
                {
                    os.AppendMethod(m);
                }
                constructorValue = os.GetJavaScriptValue();
            }
            prototypeValue = JavaScriptValue.CreateObject();

            // statics
            constructorValue.SetIndexedProperty(JavaScriptValue.FromString("toString"),
                JavaScriptValue.CreateFunction(GetSavedString, GCHandle.ToIntPtr(GCHandle.Alloc(type.FullName))));
            AssignMethodProc(constructorValue,
                type.GetMethods(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Static));
            AssignFieldProc(constructorValue,
                type.GetFields(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Static));
            AssignPropertyProc(constructorValue,
                type.GetProperties(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Static));
            // instances
            prototypeValue.SetIndexedProperty(JavaScriptValue.FromString("toString"),
                JavaScriptValue.CreateFunction(GetSavedString, GCHandle.ToIntPtr(GCHandle.Alloc(type.FullName + " Instance"))));
            AssignMethodProc(prototypeValue,
                type.GetMethods(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance));
            AssignFieldProc(prototypeValue,
                type.GetFields(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance));
            AssignPropertyProc(prototypeValue,
                type.GetProperties(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance));

            constructorValue.SetIndexedProperty(JavaScriptValue.FromString("prototype"), prototypeValue);

            if (baseTypeWrapper != null)
            {
                constructorValue.Prototype = baseTypeWrapper.constructorValue;
                prototypeValue.Prototype = baseTypeWrapper.prototypeValue;
            }
        }

        static JavaScriptValue GetSavedString(JavaScriptValue callee,
            [MarshalAs(UnmanagedType.U1)] bool isConstructCall,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] JavaScriptValue[] arguments,
            ushort argumentCount,
            IntPtr callbackData)
        {
            try
            {
                return JavaScriptValue.FromString((string)GCHandle.FromIntPtr(callbackData).Target);
            }
            catch (Exception e)
            {
                Native.JsSetException(JavaScriptValue.CreateError(JavaScriptValue.FromString(e.ToString())));
                return JavaScriptValue.Invalid;
            }
        }

        void AssignMethodProc(JavaScriptValue setTo, MethodInfo[] methods)
        {
            var methodDic = new Dictionary<string, List<MethodInfo>>();
            foreach (var m in methods)
            {
                if (m.IsSpecialName)
                    continue;
                if (m.IsGenericMethodDefinition)
                    continue;
                if (m.IsGenericMethod)
                    continue;
                if (!methodDic.ContainsKey(m.Name))
                    methodDic[m.Name] = new List<MethodInfo>();
                methodDic[m.Name].Add(m);
            }
            foreach (var methodName in methodDic.Keys)
            {
                var ms = methodDic[methodName];
                if (ms.Count == 1)
                {
                    var m = ms[0];
                    var smw = (m.IsStatic) ? (FunctionWrapper)new StaticMethodWrapper(m) : (FunctionWrapper)new InstanceMethodWrapper(m);
                    setTo.SetIndexedProperty(JavaScriptValue.FromString(m.Name), smw.GetJavaScriptValue());
                }
                else
                {
                    var os = new OverloadSelector();
                    foreach (var m in ms)
                    {
                        os.AppendMethod(m);
                    }
                    setTo.SetIndexedProperty(JavaScriptValue.FromString(os.GetName()), os.GetJavaScriptValue());
                }
            }
        }

        void AssignFieldProc(JavaScriptValue setTo, FieldInfo[] fields)
        {
            var getpropid = JavaScriptPropertyId.FromString("get");
            var setpropid = JavaScriptPropertyId.FromString("set");
            foreach (var f in fields)
            {
                if (f.IsSpecialName)
                    continue;
                var desc = JavaScriptValue.CreateObject();
                var id = JavaScriptPropertyId.FromString(f.Name);
                var proxy = new FieldProxy(f);
                desc.SetProperty(getpropid, JavaScriptValue.CreateFunction(FieldProxy.FieldGetter, GCHandle.ToIntPtr(GCHandle.Alloc(proxy))), true);
                desc.SetProperty(setpropid, JavaScriptValue.CreateFunction(FieldProxy.FieldSetter, GCHandle.ToIntPtr(GCHandle.Alloc(proxy))), true);
                setTo.DefineProperty(id, desc);
            }
        }
        class FieldProxy
        {
            FieldInfo fi;
            public FieldProxy(FieldInfo fi)
            {
                this.fi = fi;
            }
            Func<JavaScriptValue, object> getdg;
            Action<JavaScriptValue, JavaScriptValue> setdg;

            public static JavaScriptValue FieldGetter(JavaScriptValue callee,
                [MarshalAs(UnmanagedType.U1)] bool isConstructCall,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] JavaScriptValue[] arguments,
                ushort argumentCount,
                IntPtr callbackData)
            {
                try
                {
                    var that = (FieldProxy)GCHandle.FromIntPtr(callbackData).Target;
                    if (that.getdg == null)
                    {
                        that.getdg = (obj) =>
                        {
                            var objv = FunctionWrapper.Conv(obj, that.fi.DeclaringType);
                            if (that.fi.IsStatic)
                            {
                                return that.fi.GetValue(null);
                            }
                            else
                            {
                                return that.fi.GetValue(objv);
                            }
                        };
                    }
                    return JSValue.FromObject(that.getdg(arguments[0])).rawvalue;
                }
                catch (Exception e)
                {
                    Native.JsSetException(JavaScriptValue.CreateError(JavaScriptValue.FromString(e.ToString())));
                    return JavaScriptValue.Invalid;
                }
            }

            public static JavaScriptValue FieldSetter(JavaScriptValue callee,
                [MarshalAs(UnmanagedType.U1)] bool isConstructCall,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] JavaScriptValue[] arguments,
                ushort argumentCount,
                IntPtr callbackData)
            {
                try
                {
                    var that = (FieldProxy)GCHandle.FromIntPtr(callbackData).Target;
                    if (that.setdg == null)
                    {
                        that.setdg = (obj, val) =>
                        {
                            var valv = FunctionWrapper.Conv(val, that.fi.FieldType);
                            if (that.fi.IsStatic)
                            {
                                that.fi.SetValue(null, valv);
                            }
                            else
                            {
                                var objv = FunctionWrapper.Conv(obj, that.fi.DeclaringType);
                                that.fi.SetValue(objv, valv);
                            }
                        };
                    }
                    that.setdg(arguments[0], arguments[1]);
                    return arguments[1];
                }
                catch (Exception e)
                {
                    Native.JsSetException(JavaScriptValue.CreateError(JavaScriptValue.FromString(e.ToString())));
                    return JavaScriptValue.Invalid;
                }
            }
        }


        void AssignPropertyProc(JavaScriptValue setTo, PropertyInfo[] props)
        {
            var getpropid = JavaScriptPropertyId.FromString("get");
            var setpropid = JavaScriptPropertyId.FromString("set");
            foreach (var info in props)
            {
                if (info.IsSpecialName)
                    continue;
                var desc = JavaScriptValue.CreateObject();
                var id = JavaScriptPropertyId.FromString(info.Name);
                var proxy = new PropertyProxy(info);
                desc.SetProperty(getpropid, JavaScriptValue.CreateFunction(PropertyProxy.PropertyGetter, GCHandle.ToIntPtr(GCHandle.Alloc(proxy))), true);
                desc.SetProperty(setpropid, JavaScriptValue.CreateFunction(PropertyProxy.PropertySetter, GCHandle.ToIntPtr(GCHandle.Alloc(proxy))), true);
                setTo.DefineProperty(id, desc);
            }
        }
        class PropertyProxy
        {
            PropertyInfo pi;
            public PropertyProxy(PropertyInfo pi)
            {
                this.pi = pi;
            }
            Func<JavaScriptValue, object> getdg;
            Action<JavaScriptValue, JavaScriptValue> setdg;

            public static JavaScriptValue PropertyGetter(JavaScriptValue callee,
                [MarshalAs(UnmanagedType.U1)] bool isConstructCall,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] JavaScriptValue[] arguments,
                ushort argumentCount,
                IntPtr callbackData)
            {
                try
                {
                    var that = (PropertyProxy)GCHandle.FromIntPtr(callbackData).Target;
                    if (that.getdg == null)
                    {
                        var gettermethod = that.pi.GetGetMethod();
                        that.getdg = (obj) =>
                        {
                            if (gettermethod.IsStatic)
                            {
                                return gettermethod.Invoke(null, new object[0]);
                            }
                            else
                            {
                                var objv = FunctionWrapper.Conv(obj, that.pi.DeclaringType);
                                return gettermethod.Invoke(objv, new object[0]);
                            }
                        };
                    }
                    return JSValue.FromObject(that.getdg(arguments[0])).rawvalue;
                }
                catch (Exception e)
                {
                    Native.JsSetException(JavaScriptValue.CreateError(JavaScriptValue.FromString(e.ToString())));
                    return JavaScriptValue.Invalid;
                }
            }

            public static JavaScriptValue PropertySetter(JavaScriptValue callee,
                [MarshalAs(UnmanagedType.U1)] bool isConstructCall,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] JavaScriptValue[] arguments,
                ushort argumentCount,
                IntPtr callbackData)
            {
                try
                {
                    var that = (PropertyProxy)GCHandle.FromIntPtr(callbackData).Target;
                    if (that.setdg == null)
                    {
                        var settermethod = that.pi.GetSetMethod();
                        that.setdg = (obj, val) =>
                        {
                            var valv = FunctionWrapper.Conv(val, that.pi.PropertyType);
                            if (settermethod.IsStatic)
                            {
                                settermethod.Invoke(null, new object[] { valv });
                            }
                            else
                            {
                                var objv = FunctionWrapper.Conv(obj, that.pi.DeclaringType);
                                settermethod.Invoke(objv, new object[] { valv });
                            }
                        };
                    }
                    that.setdg(arguments[0], arguments[1]);
                    return arguments[1];
                }
                catch (Exception e)
                {
                    Native.JsSetException(JavaScriptValue.CreateError(JavaScriptValue.FromString(e.ToString())));
                    return JavaScriptValue.Invalid;
                }
            }
        }

        static JavaScriptValue NoConstructor(JavaScriptValue callee,
            [MarshalAs(UnmanagedType.U1)] bool isConstructCall,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] JavaScriptValue[] arguments,
            ushort argumentCount,
            IntPtr callbackData)
        {
            try
            {
                var that = (TypeWrapper)GCHandle.FromIntPtr(callbackData).Target;
                if (that.type.IsValueType)
                {
                    return JSValue.FromObject(Activator.CreateInstance(that.type)).rawvalue;
                }
                else
                {
                    Native.JsSetException(JavaScriptValue.CreateError(JavaScriptValue.FromString(that.type.Name + " has no constructor")));
                    return JavaScriptValue.Invalid;
                }
            }
            catch (Exception e)
            {
                Native.JsSetException(JavaScriptValue.CreateError(JavaScriptValue.FromString(e.ToString())));
                return JavaScriptValue.Invalid;
            }
        }
    }
}
