﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenMod.API;

namespace OpenMod.Core.Helpers
{
    [OpenModInternal]
    public static class ReflectionExtensions
    {
        public static MethodBase GetCallingMethod(Type[] skipTypes = null, MethodBase[] skipMethods = null, bool applyAsyncMethodPatch = true)
        {
            var skipList = new List<Type>(skipTypes ?? new Type[0]) { typeof(ReflectionExtensions) };

            var st = new StackTrace();
            var frameTarget = (StackFrame)null;
            for (var i = 0; i < st.FrameCount; i++)
            {
                var frame = st.GetFrame(i);
                var frameMethod = frame.GetMethod();
                if (frameMethod == null)
                    continue;

                // Hot fix for async Task methods:
                // If current frame method is called "MoveNext" and parent frame is from "AsyncMethodBuilderCore" type 
                //   it's an async method wrapper, so we need to skip these two frames to get the original calling async method
                // Tested on .NET Core 2.1; should be tested on full .NET and mono too
                if (applyAsyncMethodPatch && frameMethod is MethodInfo currentMethodFrameInfo && currentMethodFrameInfo.Name == "MoveNext")
                {
                    var tmpIndex = i;
                    var frameOriginal = frame;

                    frame = st.GetFrame(++tmpIndex);
                    frameMethod = frame.GetMethod();

                    // Check parent frame - if its from AsyncMethodBuilderCore, its definitely an async Task
                    if (frameMethod is MethodInfo parentFrameMethodInfo &&
                        (parentFrameMethodInfo.DeclaringType?.Name == "AsyncMethodBuilderCore"
                            || parentFrameMethodInfo.DeclaringType?.Name == "AsyncTaskMethodBuilder"))
                    {
                        frame = st.GetFrame(++tmpIndex);
                        frameMethod = frame.GetMethod();

                        i = tmpIndex;
                    }
                    else
                    {
                        //Restore original frame
                        frame = frameOriginal;
                        frameMethod = frameOriginal.GetMethod();
                    }
                }

                if (skipList.Any(c => c == frameMethod?.DeclaringType))
                    continue;

                if (skipMethods?.Any(c => c == frameMethod) ?? false)
                    continue;

                frameTarget = frame;
                break;
            }

            return frameTarget?.GetMethod();
        }

        public static MethodBase GetCallingMethod(params Assembly[] skipAssemblies)
        {
            var st = new StackTrace();
            var frameTarget = (StackFrame)null;
            for (var i = 0; i < st.FrameCount; i++)
            {
                var frame = st.GetFrame(i);
                if (skipAssemblies.Any(c => Equals(c, frame.GetMethod()?.DeclaringType?.Assembly)))
                    continue;

                frameTarget = frame;
            }

            return frameTarget?.GetMethod();
        }

        public static IEnumerable<Type> GetTypeHierarchy(this Type type)
        {
            var types = new List<Type> { type };
            while ((type = type.BaseType) != null)
            {
                types.Add(type);
            }

            return types;
        }

        public static IEnumerable<Type> FindAllTypes(this Assembly @object, bool includeAbstractAndInterfaces = false)
        {
            try
            {
                return @object.GetTypes()
                              .Where(c => includeAbstractAndInterfaces || !c.IsAbstract && !c.IsInterface);
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }

        public static IEnumerable<Type> FindTypes<T>(this Assembly assembly, bool includeAbstractAndInterfaces = false)
        {
            return assembly.FindAllTypes(includeAbstractAndInterfaces).Where(c => typeof(T).IsAssignableFrom(c));
        }

        public static string GetVersionIndependentName(string name)
        {
            return GetVersionIndependentName(name, out _);
        }

        private static readonly Regex VersionRegex = new Regex("Version=(?<version>.+?), ", RegexOptions.Compiled);
        public static string GetVersionIndependentName(string name, out string extractedVersion)
        {
            var match = VersionRegex.Match(name);
            extractedVersion = match.Groups[1].Value;
            return VersionRegex.Replace(name, string.Empty);
        }

        public static string GetDebugName(this MethodBase mb)
        {
            if (mb is MemberInfo mi && mi.DeclaringType != null) return mi.DeclaringType.Name + "." + mi.Name;

            return "<anonymous>#" + mb.Name;
        }

        public static async Task InvokeWithTaskSupportAsync(this MethodBase method, object instance, object[] @params)
        {
            var isAsync = false;
            if (method is MethodInfo methodInfo)
            {
                var returntype = methodInfo.ReturnType;
                isAsync = typeof(Task).IsAssignableFrom(returntype);
            }

            if (isAsync)
            {
                var task = (Task)method.Invoke(instance, @params);
                await task;
                return;
            }

            method.Invoke(instance, @params.ToArray());
        }

        public static T ToObject<T>(this Dictionary<object, object> dict)
        {
            const BindingFlags bindingFlags =
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.IgnoreCase;

            var type = typeof(T);
            var obj = Activator.CreateInstance(type);

            foreach (var kv in dict)
            {
                var prop = type.GetProperty(kv.Key.ToString(), bindingFlags);
                if (prop != null)
                {
                    prop.SetValue(obj, kv.Value);
                    continue;
                }

                var field = type.GetField(kv.Key.ToString(), bindingFlags);
                field?.SetValue(obj, kv.Value);
            }

            return (T)obj;
        }

        public static bool HasConversionOperator(this Type from, Type to)
        {
            UnaryExpression BodyFunction(Expression body) => Expression.Convert(body, to);
            ParameterExpression inp = Expression.Parameter(from, "inp");
            try
            {
                // If this succeeds then we can cast 'from' type to 'to' type using implicit coercion
                Expression.Lambda(BodyFunction(inp), inp).Compile();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}