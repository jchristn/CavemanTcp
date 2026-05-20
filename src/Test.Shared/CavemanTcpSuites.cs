namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Touchstone.Core;

    public static class CavemanTcpSuites
    {
        private static readonly string[] _SslScenarioNames =
        {
            nameof(CavemanTcpScenarios.IpPortPfxConstructorsRoundTrip),
            nameof(CavemanTcpScenarios.SslValidationFailureRaisesClientExceptionEvent),
            nameof(CavemanTcpScenarios.SslRoundTripWithCertificateObject),
            nameof(CavemanTcpScenarios.SslRoundTripWithPfxConstructors)
        };

        private static readonly string[] _ValidationScenarioNames =
        {
            nameof(CavemanTcpScenarios.ClientReadAndStreamApisThrowWhenNotConnected),
            nameof(CavemanTcpScenarios.ClientSendReturnsDisconnectedWhenNotConnected),
            nameof(CavemanTcpScenarios.PropertySettersRetainAssignedInstances),
            nameof(CavemanTcpScenarios.ResultObjectsExposeExpectedDefaultsAndConstructors),
            nameof(CavemanTcpScenarios.SettingsAndKeepaliveDefaultsAreExpected),
            nameof(CavemanTcpScenarios.StringOverloadsRejectNullOrEmptyPayloadsWhenConnected),
            nameof(CavemanTcpScenarios.ValidationGuardsCoverConstructorsSettingsAndStreams)
        };

        private static readonly IReadOnlyList<TestSuiteDescriptor> _All = BuildSuites();

        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return _All; }
        }

        private static IReadOnlyList<TestSuiteDescriptor> BuildSuites()
        {
            return new List<TestSuiteDescriptor>
            {
                new TestSuiteDescriptor(
                    "regression",
                    "CavemanTcp Regression",
                    BuildCases(includeOnlySslScenarios: false, includeOnlyValidationScenarios: false)),
                new TestSuiteDescriptor(
                    "ssl",
                    "SSL",
                    BuildCases(includeOnlySslScenarios: true)),
                new TestSuiteDescriptor(
                    "validation",
                    "Validation",
                    BuildCases(includeOnlyValidationScenarios: true))
            };
        }

        private static IReadOnlyList<TestCaseDescriptor> BuildCases(bool includeOnlySslScenarios = false, bool includeOnlyValidationScenarios = false)
        {
            IEnumerable<MethodInfo> methods = typeof(CavemanTcpScenarios)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.ReturnType == typeof(void) || m.ReturnType == typeof(System.Threading.Tasks.Task))
                .Where(m => m.GetParameters().Length == 0);

            if (includeOnlySslScenarios)
            {
                methods = methods.Where(m => _SslScenarioNames.Contains(m.Name, StringComparer.Ordinal));
            }
            else if (includeOnlyValidationScenarios)
            {
                methods = methods.Where(m => _ValidationScenarioNames.Contains(m.Name, StringComparer.Ordinal));
            }
            else
            {
                methods = methods.Where(m =>
                    !_SslScenarioNames.Contains(m.Name, StringComparer.Ordinal)
                    && !_ValidationScenarioNames.Contains(m.Name, StringComparer.Ordinal));
            }

            string suiteId =
                includeOnlySslScenarios ? "ssl" :
                includeOnlyValidationScenarios ? "validation" :
                "regression";

            return methods
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .Select(m => new TestCaseDescriptor(
                    suiteId,
                    m.Name,
                    ToDisplayName(m.Name),
                    token =>
                    {
                        try
                        {
                            object result = m.Invoke(null, null);
                            if (result is System.Threading.Tasks.Task task) return task;
                            return System.Threading.Tasks.Task.CompletedTask;
                        }
                        catch (TargetInvocationException e) when (e.InnerException != null)
                        {
                            return System.Threading.Tasks.Task.FromException(e.InnerException);
                        }
                    }))
                .ToList();
        }

        private static string ToDisplayName(string methodName)
        {
            if (String.IsNullOrEmpty(methodName)) return methodName;

            List<char> chars = new List<char>(methodName.Length + 8);
            chars.Add(methodName[0]);

            for (int i = 1; i < methodName.Length; i++)
            {
                char current = methodName[i];
                if (Char.IsUpper(current) && !Char.IsUpper(methodName[i - 1]))
                {
                    chars.Add(' ');
                }

                chars.Add(current);
            }

            return new string(chars.ToArray());
        }
    }
}
