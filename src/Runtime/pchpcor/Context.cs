﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pchp.Core.Utilities;
using System.Reflection;

namespace Pchp.Core
{
    using TFunctionsMap = Context.HandleMap<RuntimeMethodHandle, Providers.RuntimeMethodHandleComparer, Providers.OrdinalIgnoreCaseStringComparer>;
    using TTypesMap = Context.HandleMap<Type, Providers.TypeComparer, Providers.OrdinalIgnoreCaseStringComparer>;

    /// <summary>
    /// Runtime context for a PHP application.
    /// </summary>
    /// <remarks>
    /// The object represents a current Web request or the application run.
    /// Its instance is passed to all PHP function.
    /// The context is not thread safe.
    /// </remarks>
    public partial class Context : IDisposable
    {
        #region Create

        protected Context()
        {
            _functions = new TFunctionsMap(FunctionRedeclared);
            _types = new TTypesMap(TypeRedeclared);
            _statics = new object[_staticsCount];

            _globals = new PhpArray();
            // TODO: InitGlobalVariables(); //_globals.SetItemAlias(new IntStringKey("GLOBALS"), new PhpAlias(PhpValue.Create(_globals)));
        }

        /// <summary>
        /// Creates context to be used within a console application.
        /// </summary>
        public static Context CreateConsole()
        {
            return new Context();
            // TODO: Add console output filter
        }

        public static Context CreateEmpty()
        {
            return new Context();
        }

        #endregion

        #region Symbols

        /// <summary>
        /// Map of global functions.
        /// </summary>
        TFunctionsMap _functions;

        /// <summary>
        /// Map of global types.
        /// </summary>
        TTypesMap _types;

        // TODO: global constants

        /// <summary>
        /// Internal method to be used by loader to load referenced symbols.
        /// </summary>
        /// <typeparam name="TScript"><c>&lt;Script&gt;</c> type in compiled assembly. The type contains static methods for enumerating referenced symbols.</typeparam>
        public static void AddScriptReference<TScript>() => AddScriptReference(typeof(TScript));

        private static void AddScriptReference(Type tscript)
        {
            Debug.Assert(tscript != null);
            Debug.Assert(tscript.Name == "<Script>");

            TFunctionsMap.LazyAddReferencedSymbols(() =>
            {
                tscript.GetTypeInfo().GetDeclaredMethod("EnumerateReferencedFunctions")
                    .Invoke(null, new object[] { new Action<string, RuntimeMethodHandle>(TFunctionsMap.AddReferencedSymbol) });
            });
        }

        /// <summary>
        /// Declare a runtime function.
        /// </summary>
        /// <param name="index">Index variable.</param>
        /// <param name="name">Fuction name.</param>
        /// <param name="handle">Function runtime handle.</param>
        public void DeclareFunction(ref int index, string name, RuntimeMethodHandle handle)
        {
            _functions.Declare(ref index, name, handle);
        }

        public void AssertFunctionDeclared(ref int index, string name, RuntimeMethodHandle handle)
        {
            if (!_functions.IsDeclared(ref index, name, handle))
            {
                // TODO: ErrCode function is not declared
            }
        }

        /// <summary>
        /// Gets declared function with given name. In case of more items they are considered as overloads.
        /// </summary>
        internal RuntimeMethodHandle[] GetDeclaredFunction(string name) => _functions.TryGetHandle(name);

        /// <summary>
        /// Declare a runtime type.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="name">Type name.</param>
        public void DeclareType<T>(string name)
        {
            _types.Declare(ref IndexHolder<T>.Index, name, typeof(T));
        }

        public void AssertTypeDeclared<T>(string name)
        {
            if (!_types.IsDeclared(ref IndexHolder<T>.Index, name, typeof(T)))
            {
                // TODO: ErrCode type is not declared
            }
        }

        /// <summary>
        /// Gets declared function with given name. In case of more items they are considered as overloads.
        /// </summary>
        internal Type[] GetDeclaredType(string name) => _types.TryGetHandle(name);

        void FunctionRedeclared(RuntimeMethodHandle handle)
        {
            // TODO: ErrCode & throw
            throw new InvalidOperationException($"Function {System.Reflection.MethodBase.GetMethodFromHandle(handle).Name} redeclared!");
        }

        void TypeRedeclared(Type handle)
        {
            Debug.Assert(handle != null);

            // TODO: ErrCode & throw
            throw new InvalidOperationException($"Type {handle.FullName} redeclared!");
        }

        #endregion

        #region Inclusions

        /// <summary>
        /// Used by runtime.
        /// Determines whether the <c>include_once</c>/<c>require_once</c> is allowed to proceed.
        /// </summary>
        public bool IncludeOnceAllowed<TScript>()
        {
            // TODO: => !IsIncluded(IndexHolder<TScript>.Index)

            return true;
        }

        #endregion

        #region GetStatic

        /// <summary>
        /// Helper generic class holding an app static index to array of static objects.
        /// </summary>
        /// <typeparam name="T">Type of object kept as context static.</typeparam>
        static class IndexHolder<T>
        {
            /// <summary>
            /// Index of the object of type <typeparamref name="T"/>.
            /// </summary>
            public static int Index;
        }

        /// <summary>
        /// Gets static object instance within the context with given index.
        /// Initializes the index with new unique value if necessary.
        /// </summary>
        T GetStatic<T>(ref int idx) where T : new()
        {
            if (idx <= 0)
                idx = NewIdx();

            return GetStatic<T>(idx);
        }

        /// <summary>
        /// Gets static object instance within the context with given index.
        /// </summary>
        T GetStatic<T>(int idx) where T : new()
        {
            EnsureStaticsSize(idx);
            return GetStatic<T>(ref _statics[idx]);
        }

        /// <summary>
        /// Ensures the <see cref="_statics"/> array has sufficient size to hold <paramref name="idx"/>;
        /// </summary>
        /// <param name="idx">Index of an object to be stored within statics.</param>
        void EnsureStaticsSize(int idx)
        {
            if (_statics.Length <= idx)
            {
                Array.Resize(ref _statics, (idx + 1) * 2);
            }
        }

        /// <summary>
        /// Ensures the context static object is initialized.
        /// </summary>
        T GetStatic<T>(ref object obj) where T : new()
        {
            if (obj == null)
            {
                obj = new T();
                //if (obj is IStaticInit) ((IStaticInit)obj).Init(this);
            }

            Debug.Assert(obj is T);
            return (T)obj;
        }

        /// <summary>
        /// Gets context static object of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Type of the object to be stored within context.</typeparam>
        public T GetStatic<T>() where T : new() => GetStatic<T>(ref IndexHolder<T>.Index);

        /// <summary>
        /// Gets new index to be used within <see cref="_statics"/> array.
        /// </summary>
        int NewIdx()
        {
            int idx;

            lock (_statics)
            {
                idx = Interlocked.Increment(ref _staticsCount);
            }

            return idx;
        }

        /// <summary>
        /// Static objects within the context.
        /// Cannot be <c>null</c>.
        /// </summary>
        object[] _statics;

        /// <summary>
        /// Number of static objects so far registered within context.
        /// </summary>
        static volatile int/*!*/_staticsCount;

        #endregion

        #region Superglobals

        /// <summary>
        /// Array of global variables. Cannot be <c>null</c>.
        /// </summary>
        public PhpArray Globals
        {
            get { return _globals; }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException();  // TODO: ErrCode
                }

                _globals = value;
            }
        }
        PhpArray _globals;

        #endregion

        #region IDisposable

        public void Dispose()
        {

        }

        #endregion
    }
}
