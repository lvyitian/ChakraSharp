﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using ChakraHost.Hosting;
using System.Linq.Expressions;
using System.Reflection;


namespace ChakraSharp
{
    public class Controller: IDisposable
    {
        public JavaScriptRuntime runtime;
        public JavaScriptContext context;
        public JavaScriptSourceContext currentSourceContext;

        public Controller()
        {
            Start();
        }

        public void CollectGarbage()
        {
            runtime.CollectGarbage();
        }

        public JSValue Null
        {
            get
            {
                return JSValue.Null;
            }
        }
        public JSValue Undefined
        {
            get
            {
                return JSValue.Undefined;
            }
        }

        static void ThrowError(JavaScriptErrorCode err, string location)
        {
            var sb = new System.Text.StringBuilder();
            JavaScriptValue ex;
            bool hasEx;
            Native.ThrowIfError(Native.JsHasException(out hasEx));
            object obj=null;
            if (hasEx)
            {
                Native.ThrowIfError(Native.JsGetAndClearException(out ex));
                IntPtr p = IntPtr.Zero;
                Native.JsGetExternalData(ex, out p);
                if (p != IntPtr.Zero)
                {
                    obj = GCHandle.FromIntPtr(p).Target;
                }
                if (err == JavaScriptErrorCode.ScriptCompile)
                {
                    var message = ex.GetIndexedProperty(JavaScriptValue.FromString("message")).ConvertToString().ToString();
                    var line = ex.GetIndexedProperty(JavaScriptValue.FromString("line")).ConvertToString().ToString();
                    var column = ex.GetIndexedProperty(JavaScriptValue.FromString("column")).ConvertToString().ToString();
                    sb.AppendFormat("{0}\n   at code ({3}:{1}:{2})", message, line, column, location);
                }
                else if (err == JavaScriptErrorCode.ScriptException)
                {
                    if (ex.ValueType == JavaScriptValueType.Error ||
                        ex.ValueType == JavaScriptValueType.Object)
                    {
                        var messageobj = ex.GetIndexedProperty(JavaScriptValue.FromString("message"));
                        IntPtr messageobjex = IntPtr.Zero;
                        Native.JsGetExternalData(messageobj, out messageobjex);
                        string message;
                        if (messageobjex != IntPtr.Zero)
                        {
                            obj = GCHandle.FromIntPtr(messageobjex).Target;
                            if (obj is Exception)
                            {
                                message = ((Exception)obj).Message;
                            }
                            else
                            {
                                message = obj.ToString();
                            }
                        }
                        else
                        {
                            message = messageobj.ConvertToString().ToString();
                        }
                        var stack = ex.GetIndexedProperty(JavaScriptValue.FromString("stack")).ConvertToString().ToString();
                        sb.AppendFormat("{0}\n{1}", message, stack);
                    }
                    else
                    {
                        sb.AppendFormat("{0}", ex.ConvertToString().ToString());
                    }
                }
                else if (ex.ValueType == JavaScriptValueType.Error ||
                    ex.ValueType == JavaScriptValueType.Object)
                {
                    Console.WriteLine("else error?");
                    var errorobj = ex.GetIndexedProperty(JavaScriptValue.FromString("message"));
                    p = IntPtr.Zero;
                    Native.JsGetExternalData(errorobj, out p);
                    if (p != IntPtr.Zero)
                    {
                        obj = GCHandle.FromIntPtr(p).Target;
                    }
                    if (obj != null)
                    {
                        sb.Append(System.Convert.ToString(obj));
                    }
                    else
                    {
                        sb.Append(errorobj.ConvertToString().ToString());
                    }
                }
                else
                {
                    sb.Append(ex.ConvertToString().ToString());
                }
            }
            else
            {
                sb.Append(err);
            }
            throw new ChakraSharpException(sb.ToString(), obj as Exception);
            //return sb.ToString();
        }
        public void Execute(string js)
        {
            Evaluate(js);
        }
        public void Execute(string js, string sourceName)
        {
            JavaScriptValue result;

            var err = Native.JsRunScript(js, currentSourceContext++, sourceName, out result);
            if (err == JavaScriptErrorCode.ScriptException ||
                err == JavaScriptErrorCode.ScriptCompile ||
                err == JavaScriptErrorCode.InExceptionState)
            {
                ThrowError(err, sourceName);
                //throw new ChakraSharpException(ErrorToString(err, sourceName));
            }
            else
            {
                Native.ThrowIfError(err);
            }
        }
        public JSValue Evaluate(string js)
        {
            return Evaluate(js, "Evaluate");
        }
        public JSValue Evaluate(string js, string sourceName)
        {
            JavaScriptValue result;
            var err = Native.JsRunScript(js, currentSourceContext++, sourceName, out result);
            if (err == JavaScriptErrorCode.ScriptException ||
                err == JavaScriptErrorCode.ScriptCompile ||
                err == JavaScriptErrorCode.InExceptionState)
            {
                ThrowError(err, sourceName);
                //throw new ChakraSharpException(ErrorToString(err, sourceName));
            }
            else
            {
                Native.ThrowIfError(err);
            }
            return JSValue.Make(result);
        }


        public void Dispose()
        {
            JavaScriptContext.Current = JavaScriptContext.Invalid;
            context = JavaScriptContext.Invalid;
            runtime.Dispose();
            Port.Util.ClearCache();
        }


        public void Start()
        {
            Dispose();

            // Create a runtime. 
            //Native.ThrowIfError(Native.JsCreateRuntime(JavaScriptRuntimeAttributes.None, null, out runtime));
            
            Native.ThrowIfError(Native.JsCreateRuntime(JavaScriptRuntimeAttributes.DisableBackgroundWork, null, out runtime));
            Native.ThrowIfError(Native.JsEnableRuntimeExecution(runtime));
            // Create an execution context. 
            Native.ThrowIfError(Native.JsCreateContext(runtime, out context));

            // Now set the execution context as being the current one on this thread.
            Native.ThrowIfError(Native.JsSetCurrentContext(context));
            {
                JavaScriptValue v;
                Native.ThrowIfError(Native.JsGetGlobalObject(out v));
                Global = JSValue.Make(v);
            }
        }

        public JSValue Global;
        /*
        public JSValue Global
        {
            get
            {
                JavaScriptValue v;
                Native.ThrowIfError(Native.JsGetGlobalObject(out v));
                return new JSValue(v);
            }
        }
        */



        public JSValue WrapType<T>()
        {
            return ChakraSharp.Port.Util.WrapType(typeof(T));
        }
        public JSValue Wrap(Type t)
        {
            return ChakraSharp.Port.Util.WrapType(t);
        }
        public JSValue WrapNamespace(string namespacePath)
        {
            return ChakraSharp.Port.Util.WrapNamespace(namespacePath);
        }
        public JSValue Wrap(Delegate dg)
        {
            return ChakraSharp.Port.Util.WrapDelegate(dg);
        }
        public JSValue Wrap(MethodInfo mi)
        {
            return ChakraSharp.Port.Util.WrapMethod(mi);
        }
        public JSValue Wrap(ConstructorInfo o)
        {
            return ChakraSharp.Port.Util.WrapConstructor(o);
        }
        public JSValue WrapObject(object o)
        {
            return ChakraSharp.Port.Util.WrapObject(o);
        }
    }
}
