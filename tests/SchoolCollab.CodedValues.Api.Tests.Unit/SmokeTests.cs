using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.CodedValues.Api.Tests.Unit;

[TestClass]
public class SmokeTests
{
    [TestMethod]
    public void Project_loads_and_references_api_assembly()
    {
        var programType = typeof(Program);
        Assert.IsNotNull(programType);
        Assert.AreEqual("SchoolCollab.CodedValues.Api", programType.Assembly.GetName().Name);
    }
}
