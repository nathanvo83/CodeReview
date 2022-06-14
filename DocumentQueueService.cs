using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using log4net;
using QBE.SamPlus.Services.Db.QBE;
using Quartz;
using DocumentQueue = QBE.SamPlus.Services.Db.QBE.DocumentQueues;

namespace QBE.DocumentGeneration.Services
{

    // code review:
    // 1. Lacking dispose var/object
    // Improve: using Injection
    // 2. DocumentsStillInQueueToBeGenerated and GetDocumentsToGenerate 
    //      d.Attempts < 3
    //  using the number.
    // Improve: declare a constant and explain why it is 3.
    // 3. IsDocumentGenerated
    //      d.DocumentType == (int)documentType
    //  using force cast.
    // Improve: need try/catch to handle exception or create method Bool DocumentType.equals(DocumentType type)
    // Recommandation: create compared method
    // 4. TryAdd
    //  always return true as success.
    // Improve: Need to handle the case of additional failure
    // 


    [DisallowConcurrentExecution]
    public class DocumentQueueService : IDocumentQueue, IJob
    {
        private readonly IQbeNexusDb db;
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private readonly ConfigReader configReader;
        private readonly DocumentService documentService;
        private readonly SamWebServices samWebServices;
        public string MachineName { get; }

        public DocumentQueueService()
        {
            db = new QbeNexusDb();
            configReader = new ConfigReader();
            samWebServices = new SamWebServices();
            documentService = new DocumentService(configReader, this, samWebServices);

            MachineName = Environment.MachineName;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var documentsToGenerate = GetDocumentsToGenerate(configReader.DocumentGenerationSection.SimultaneousDocumentsToGenerate);

            while (documentsToGenerate.Any())
            {
                foreach (var document in documentsToGenerate)
                {
                    if (context.CancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var documentType = (DocumentType) document.DocumentType;

                    try
                    {
                        if (documentType == DocumentType.Wording)
                        {
                            SetWordingPaths(document);
                        }
                        else
                        {
                            var documentTemplateCode = documentService.GetDocumentTemplateCode(document.InsuranceRef, document.InsuranceFileTypeCode, documentType);

                            if (string.IsNullOrEmpty(documentTemplateCode) || !documentService.DocumentTemplateCodeExists(documentTemplateCode))
                            {
                                TryRemove(document.Id, document.InsuranceRef, documentType, 0, @"Wording\DocumentGenerationError.pdf", $"The document template code '{documentTemplateCode}' or the document template zip file could not be found for document type {documentType} - {document.InsuranceRef}", documentTemplateCode);
                            }
                            else
                            {
                                documentService.GenerateDocument(document, documentTemplateCode);
                            }
                        }
                    }

                    catch (Exception ex)
                    {
                        var reasonFailed = $"Error generating document {documentType} for {document.InsuranceRef} result {ex.Message}";
                        log.Error(reasonFailed, ex);
                        
                    }
                }

                documentsToGenerate = GetDocumentsToGenerate(configReader.DocumentGenerationSection.SimultaneousDocumentsToGenerate);
            }

            await Console.Out.WriteLineAsync($"Executed {context.Trigger.Key.Name}");
        }


        public bool DocumentsStillInQueueToBeGenerated(int insuranceFileKey)
        {
            return db.DocumentQueues.Any(d => d.InsuranceFileKey == insuranceFileKey && d.Generated == false && d.Attempts < 3);
        }

        public bool IsDocumentGenerated(int insuranceFileKey, DocumentType documentType)
        {
            return db.DocumentQueues.Any(d => d.InsuranceFileKey == insuranceFileKey 
                && d.Generated == true && d.DocumentType == (int)documentType);
        }

        public bool TryAdd(string insuranceRef, int insuranceFileKey, int insuranceFolderKey, string insuranceFileTypeCode, 
            int clientId, DocumentType documentType, string currentUsername)
        {
            db.DocumentQueues.Add(new DocumentQueue { InsuranceRef = insuranceRef, InsuranceFileKey = insuranceFileKey, InsuranceFolderKey = insuranceFolderKey,
                                                InsuranceFileTypeCode = insuranceFileTypeCode, ClientId = clientId, DocumentType = (int)documentType, Generated = false, 
                                                User = currentUsername, Date = DateTime.Now, ServerIssued = MachineName });
            db.SaveChanges();
            return true;
        }

        public void SetBackgroundJobId(int id, int backgroundJobId, string templateCode)
        {
            var document = db.DocumentQueues.FirstOrDefault(d => d.Id == id);

            if (document == null)
            {
                return;
            }

            document.Generated = false;
            document.BackgroundJobId = backgroundJobId;
            document.DocumentCode = templateCode;

            db.SaveChanges();
        }

        public void SetGenerated(int id, TimeSpan elapsed, string failureDescription, string pdfFileName)
        {
            var document = db.DocumentQueues.FirstOrDefault(d => d.Id == id);

            if (document == null)
            {
                return;
            }

            document.Generated = true;
            document.DurationMs = (int)elapsed.TotalMilliseconds;
            document.Attempts++;

            document.DateGenerated = DateTime.Now;
            document.GeneratedDocumentLocation = pdfFileName;
            log.DebugFormat("Pure successfully generated document {0} {1} - {2}", document.InsuranceRef, document.DocumentCode, pdfFileName);

            db.SaveChanges();
        }

        private List<DocumentQueue> GetDocumentsToGenerate(int numberOfDocumentsToRetrieve = 1)
        {
            var documentsToGenerate = db.DocumentQueues
                .Where(d => d.Generated == false && d.BackgroundJobId == null && d.Attempts < 3 && (d.ServerGenerated == null || d.ServerGenerated == MachineName))
                .OrderBy(d => d.Date)
                .Take(numberOfDocumentsToRetrieve).ToList();

            var saveChanges = false;
            foreach (var documentQueue in documentsToGenerate.Where(d => d.ServerGenerated == null))
            {
                documentQueue.ServerGenerated = MachineName;
                saveChanges = true;
            }

            if (saveChanges)
            {
                db.SaveChanges();
            }

            return documentsToGenerate;
        }

        private void SetWordingPaths(DocumentQueue document)
        {
            var policyType = new InsuranceReference(document.InsuranceRef).PolicyType;
            var wordingCode = documentService.GetWordingCode(document.InsuranceFileKey);
            var wordingPath = EmailQueueService.GetWordingPath(wordingCode, policyType);

            SetGenerated(document.Id, new TimeSpan(), null, wordingPath);

            log.DebugFormat("Successfully updated wording doc for {0} {1}", document.InsuranceRef, wordingPath);
        }

        public void TryRemove(int insuranceFileKey, string insuranceRef, DocumentType documentType, double duration, 
            string pdfFileName, string errorMessage, string documentCode = null)
        {
            throw new NotImplementedException();
        }

        public void SetError(int id, DocumentType documentType, string reasonFailed, int attempts = -1)
        {
            throw new NotImplementedException();
        }
    }
}
