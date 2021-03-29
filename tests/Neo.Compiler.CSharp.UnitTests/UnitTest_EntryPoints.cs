using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Neo.Compiler.CSharp.UnitTests.Utils;

namespace Neo.Compiler.MSIL.UnitTests
{
    [TestClass]
    public class UnitTest_EntryPoints
    {
        [TestMethod]
        public void Test_MultipleContracts()
        {
            using var testengine = new TestEngine();
            Assert.ThrowsException<Exception>(() => testengine.AddEntryScript("./TestClasses/Contract_MultipleContracts.cs"));
        }
    }
}
