using Amazon.XRay.Model;
using System.Threading.Tasks;

namespace AmazonSDKWrapper
{
    public interface IXRayClient
    {
        Task<GetTraceSummariesResponse> GetTraceSummariesAsync(GetTraceSummariesRequest request);
        Task<BatchGetTracesResponse> BatchGetTracesAsync(BatchGetTracesRequest request);
        Task<PutTraceSegmentsResponse> PutTraceSegmentsAsync(PutTraceSegmentsRequest request);
    }
}