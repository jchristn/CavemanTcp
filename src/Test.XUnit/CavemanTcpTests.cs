namespace Test.XUnit
{
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using Xunit;

    [CollectionDefinition("CavemanTcp", DisableParallelization = true)]
    public sealed class CavemanTcpCollection
    {
    }

    [Collection("CavemanTcp")]
    public sealed class CavemanTcpTests
    {
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            TheoryData<TestCaseDescriptor> data = new TheoryData<TestCaseDescriptor>();

            foreach (TestSuiteDescriptor suite in CavemanTcpSuites.All)
            {
                foreach (TestCaseDescriptor testCase in suite.Cases)
                {
                    if (!testCase.Skip) data.Add(testCase);
                }
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task Run(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }
}
