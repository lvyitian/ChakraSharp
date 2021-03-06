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
    public class ChakraSharpException : Exception
    {
        public ChakraSharpException()
        {
        }

        public ChakraSharpException(string message)
        : base(message)
        {
        }

        public ChakraSharpException(string message, Exception inner)
        : base(message, inner)
        {
        }
    }

    public class ExceptionUtil
    {
        static public JavaScriptValue SetJSException(Exception e)
        {
            var v = JavaScriptValue.CreateExternalObject(GCHandle.ToIntPtr(GCHandle.Alloc(e)), FreeDg);
            v.AddRef();
            v.SetIndexedProperty(JavaScriptValue.FromString("toString"), JavaScriptValue.FromString(e.ToString()));
            Native.JsSetException(JavaScriptValue.CreateError(v));
            return JavaScriptValue.Invalid;
        }
        static JavaScriptObjectFinalizeCallback FreeDg = Free;
        static void Free(IntPtr p)
        {
            try
            {
                //GCHandle.FromIntPtr(p).Free();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}
