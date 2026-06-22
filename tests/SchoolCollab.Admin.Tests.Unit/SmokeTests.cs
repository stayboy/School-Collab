using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

[TestClass]
public class SmokeTests
{
    [TestMethod]
    public void Project_loads_and_test_runner_executes()
    {
        // Placeholder smoke test ensuring the test project is wired into the solution.
        var projectName = typeof(SmokeTests).Assembly.GetName().Name;
        Assert.AreEqual("SchoolCollab.Admin.Tests.Unit", projectName);
    }
}
