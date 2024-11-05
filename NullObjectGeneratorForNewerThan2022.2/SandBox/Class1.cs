using System;
using NullObjectGenerator;

namespace SandBox
{
    [InheritsToNullObj(NullObjLog.ThrowException)]
    public class Class1 : IHoge
    {
        private string _testStr;

        public string TestStr => _testStr;

        public void Test(int tes, float tes2)
        {
            throw new NotImplementedException();
        }
    }

    public interface IHoge
    {
        string TestStr { get; }
        void Test(int tes, float tes2);

    }

    [InterfaceToNullObj(NullObjLog.ThrowException)]
    public interface IFuga : IFugaBase
    {
        uint Uin { get; set; }
        string GetTestStr(string source);
    }
    
    public interface IFugaBase
    {
        void FugaBase();
    }
}