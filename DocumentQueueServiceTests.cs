using System.Threading.Tasks;
using Moq;
using QBE.DocumentGeneration.Services;
using Quartz;
using Xunit;

namespace QBE.DocumentGeneration.Tests
{

    // code review
    // 1. DoDocumentWorkTest
    //  There is no expected result. 
    //  What is the reason of this test case?
    // 2. Lacking a test case for others method e.g. TryAdd, TryRemove, SetError.
    // Improve: add more test case

    public class DocumentQueueServiceTests
    {
        [Fact]
        public async Task DoDocumentWorkTest()
        {
            var service = new DocumentQueueService();

            await service.Execute(new Mock<IJobExecutionContext>().Object);
        }
    }
}
