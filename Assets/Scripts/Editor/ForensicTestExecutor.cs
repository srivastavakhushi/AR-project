using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using CrimeSceneVR.Tests;

namespace CrimeSceneVR.Editor
{
    [InitializeOnLoad]
    public static class ForensicTestExecutor
    {
        private const string ResultsFilePath = "test_results.txt";

        static ForensicTestExecutor()
        {
            EditorApplication.delayCall += () =>
            {
                RunAllTests(silentIfPass: false);
            };
        }

        [MenuItem("Forensic/Execute All Unit Tests")]
        public static void RunTestsMenu()
        {
            RunAllTests(silentIfPass: false);
        }

        public static void RunAllTests(bool silentIfPass = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine($"FORENSIC UNIT TEST EXECUTION REPORT - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine("Unity Version: " + Application.unityVersion);
            sb.AppendLine("================================================================================\n");

            int totalTests = 0;
            int passedTests = 0;
            int failedTests = 0;

            // 1. Person 2 Tests
            RunFixture<Person2VRInteractionTests>("Person 2: VR Controller Evidence Interaction", ref totalTests, ref passedTests, ref failedTests, sb);

            // 2. Person 3 Tests
            RunFixture<Person3ResearchTests>("Person 3: Telemetry, Kendall's Tau & Research Analytics", ref totalTests, ref passedTests, ref failedTests, sb);

            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine($"SUMMARY: Total: {totalTests} | Passed: {passedTests} | Failed: {failedTests}");
            sb.AppendLine("--------------------------------------------------------------------------------");

            string report = sb.ToString();
            File.WriteAllText(ResultsFilePath, report);
            Debug.Log($"[ForensicTestExecutor] All unit tests completed. Passed: {passedTests}/{totalTests}. Results saved to {ResultsFilePath}\n{report}");
        }

        private static void RunFixture<T>(string fixtureTitle, ref int total, ref int passed, ref int failed, StringBuilder sb) where T : new()
        {
            sb.AppendLine($"--- {fixtureTitle} ({typeof(T).Name}) ---");

            Type fixtureType = typeof(T);
            MethodInfo setUp = fixtureType.GetMethod("SetUp", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo tearDown = fixtureType.GetMethod("TearDown", BindingFlags.Public | BindingFlags.Instance);

            var methods = fixtureType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var method in methods)
            {
                if (method.GetCustomAttribute<NUnit.Framework.TestAttribute>() == null)
                    continue;

                total++;
                T instance = new T();
                try
                {
                    setUp?.Invoke(instance, null);
                    method.Invoke(instance, null);
                    passed++;
                    sb.AppendLine($"  [PASS] {method.Name}");
                }
                catch (Exception ex)
                {
                    failed++;
                    Exception inner = ex.InnerException ?? ex;
                    sb.AppendLine($"  [FAIL] {method.Name} - {inner.Message}");
                    Debug.LogError($"[ForensicTestExecutor] Test Failed: {method.Name}\n{inner}");
                }
                finally
                {
                    try
                    {
                        tearDown?.Invoke(instance, null);
                    }
                    catch (Exception tdEx)
                    {
                        Debug.LogWarning($"[ForensicTestExecutor] TearDown failed for {method.Name}: {tdEx.Message}");
                    }
                }
            }
            sb.AppendLine();
        }
    }
}
