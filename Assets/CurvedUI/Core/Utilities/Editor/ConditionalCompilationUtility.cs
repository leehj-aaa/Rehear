using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace CurvedUI.Core.Utilities.Editor
{
    /// <summary>
    /// Conditional Compilation Utility (CCU) by Unity
    /// https://github.com/Unity-Technologies/EditorXR/blob/development/Scripts/Utilities/Editor/ConditionalCompilationUtility.cs
    /// 
    /// The Conditional Compilation Utility (CCU) will add defines to the build settings once dependendent classes have been detected. 
    /// In order for this to be specified in any project without the project needing to include the CCU, at least one custom attribute 
    /// must be created in the following form:
    ///
    /// [Conditional(UNITY_CCU)]                                    // | This is necessary for CCU to pick up the right attributes
    /// public class OptionalDependencyAttribute : Attribute        // | Must derive from System.Attribute
    /// {
    ///     public string dependentClass;                           // | Required field specifying the fully qualified dependent class
    ///     public string define;                                   // | Required field specifying the define to add
    /// }
    ///
    /// Then, simply specify the assembly attribute(s) you created:
    /// [assembly: OptionalDependency("UnityEngine.InputNew.InputSystem", "USE_NEW_INPUT")]
    /// [assembly: OptionalDependency("Valve.VR.IVRSystem", "ENABLE_STEAMVR_INPUT")]
    /// </summary>
    [InitializeOnLoad]
    public static class ConditionalCompilationUtility
    {
        private const string KEnableCcu = "UNITY_CCU";

        public static bool enabled => DefineSymbolUtility.ContainsDefineSymbol(KEnableCcu);
        
        public static string[] defines { private set; get; }

        static ConditionalCompilationUtility()
        {
            DefineSymbolUtility.AddDefineSymbolIfMissing(KEnableCcu);

            var ccuDefines = new List<string> { KEnableCcu };

            var conditionalAttributeType = typeof(ConditionalAttribute);

            const string kDependentClass = "dependentClass";
            const string kDefine = "define";

            var attributeTypes = GetAssignableTypes(typeof(Attribute), type =>
            {
                var conditionals = (ConditionalAttribute[])type.GetCustomAttributes(conditionalAttributeType, true);

                foreach (var conditional in conditionals)
                {
                    if (string.Equals(conditional.ConditionString, KEnableCcu, StringComparison.OrdinalIgnoreCase))
                    {
                        var dependentClassField = type.GetField(kDependentClass);
                        if (dependentClassField == null)
                        {
                            Debug.LogErrorFormat("[CCU] Attribute type {0} missing field: {1}", type.Name, kDependentClass);
                            return false;
                        }

                        var defineField = type.GetField(kDefine);
                        if (defineField == null)
                        {
                            Debug.LogErrorFormat("[CCU] Attribute type {0} missing field: {1}", type.Name, kDefine);
                            return false;
                        }
                    }
                    return true;
                }

                return false;
            });

            var dependencies = new Dictionary<string, string>();
            ForEachAssembly(assembly =>
            {
                var typeAttributes = assembly.GetCustomAttributes(false).Cast<Attribute>();
                foreach (var typeAttribute in typeAttributes)
                {
                    if (!attributeTypes.Contains(typeAttribute.GetType())) continue;
                    
                    var t = typeAttribute.GetType();

                    // These fields were already validated in a previous step
                    var dependentClass = t.GetField(kDependentClass).GetValue(typeAttribute) as string;
                    var define = t.GetField(kDefine).GetValue(typeAttribute) as string;

                    if (!string.IsNullOrEmpty(dependentClass) && !string.IsNullOrEmpty(define))
                        dependencies.TryAdd(dependentClass, define);
                }
            });
            
            ForEachAssembly(assembly =>
            {
                foreach (var (key, define) in dependencies)
                {
                    if (assembly.GetType(key) is null) continue;

                    ccuDefines.Add(define);
                }
            });

            defines = ccuDefines.ToArray();
            
            DefineSymbolUtility.AddDefineSymbolIfMissing(defines);
        }

        static void ForEachAssembly(Action<Assembly> callback)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                try
                {
                    callback(assembly);
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip any assemblies that don't load properly
                    continue;
                }
            }
        }

        static void ForEachType(Action<Type> callback)
        {
            ForEachAssembly(assembly =>
            {
                var types = assembly.GetTypes();
                foreach (var t in types)
                    callback(t);
            });
        }

        static IEnumerable<Type> GetAssignableTypes(Type type, Func<Type, bool> predicate = null)
        {
            var list = new List<Type>();
            ForEachType(t =>
            {
                if (type.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract && (predicate == null || predicate(t)))
                    list.Add(t);
            });

            return list;
        }
    }
}