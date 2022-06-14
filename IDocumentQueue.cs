using System;

namespace QBE.DocumentGeneration.Services
{

    // code review
    // 1. Naming issue
    // Improve: IDocumentQueueService
    // 2. May need summary for others
    //

    public interface IDocumentQueue
    {
        void TryRemove(int insuranceFileKey, string insuranceRef, DocumentType documentType, double duration, 
            string pdfFileName, string errorMessage, string documentCode = null);
        void SetError(int id, DocumentType documentType, string reasonFailed, int attempts = -1);
        
        bool TryAdd(string insuranceRef, int insuranceFileKey, int insuranceFolderKey, string insuranceFileTypeCode, 
            int clientId, DocumentType documentType, string currentUsername);

        /// <summary>
        /// Finds any documents still in the queue to be generated.
        /// </summary>
        bool DocumentsStillInQueueToBeGenerated(int insuranceFileKey);

        /// <summary>
        /// Find any documents that have been generated
        /// </summary>
        bool IsDocumentGenerated(int insuranceFileKey, DocumentType documentType);

        void SetBackgroundJobId(int id, int backgroundJobId, string templateCode);
        void SetGenerated(int id, TimeSpan elapsed, string failureDescription, string pdfFileName);
    }
}