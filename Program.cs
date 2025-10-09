// Connection string and queue name from your Azure portal or environment variables
using Azure.Messaging.ServiceBus;
using BKClaimService;
using BKClaimService.Docusign;
using BKClaimService.Models;
using BKClaimService.NetDocuments;
using Dapper;
using DocuSign.eSign.Model;
using DocuSignConsoleApp.Enum;
using DocuSignConsoleApp.Model;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;
using static Dapper.SqlMapper;


class Program
{
    public static string connectionString = "Data Source=10.100.240.41; Initial Catalog=ASB_PROD; Database=ASB_PROD; User Id=KavayahUser; Password=Kavayah!@#;Encrypt=false;TrustServerCertificate=true; Max Pool Size=40;Min Pool Size=2;Pooling=true;";
    public static string DBconnectionString = "Data Source=FIRM-DB1-DEV; Initial Catalog=ASB_PROD; Database=ASB_PROD; User Id=KavayahUser; Password=Kavayah!@#;Encrypt=false;TrustServerCertificate=true; Max Pool Size=40;Min Pool Size=2;Pooling=true;";
    static async Task Main()
    {
        ClaimMaster claimMaster = new ClaimMaster(DBconnectionString);

        IEnumerable<string> envelopeIDs = await claimMaster.GetInCompleteStatusEnvelopesAsync();

        
        DocusignService _docusignService = new DocusignService(DBconnectionString);

        if (envelopeIDs.Count() > 0) { 
            
        
        IEnumerable<Envelope> envelopes =  await _docusignService.GetEnvelopesInformationFromDocuSignAsync(envelopeIDs);

        foreach (Envelope envelope in envelopes)
        {
            Console.WriteLine($" EnvelopeID:  {envelope.EnvelopeId} {envelope.Status} {envelope.StatusChangedDateTime}");
            await SyncDataBaseEnvelope(envelope);
        }
        }
        await RunJObAndUploadDocumentsToNetDocuments();

        //int eqCode = 397977;
        //IEnumerable<int> EqCodes = await claimMaster.GetEqCodesWithInRangeAsync();


        //Console.WriteLine(await claimMaster.GetClaimsByEqCode(eqCode));

        //IEnumerable<int> eqCodes = await claimMaster.GetEqCodesWithInRangeAsync();
        /*
        foreach (var eqCodeval in EqCodes)
        {
            var claims = await claimMaster.GetClaimsByEqCode(eqCodeval);
            Console.WriteLine(claims);
        }*/

        //await SyncDataBaseOffers(await claimMaster.GetClaimsByEqCode(eqCode));
    }

    
// Handle any errors when receiving messages
static Task ErrorHandler(ProcessErrorEventArgs args)
{
    Console.WriteLine($"Error receiving message: {args.Exception.Message}");
    return Task.CompletedTask;
}

    static async Task SyncDataBaseOffers(string claims)
    {
        try
        {
            
            using (var connection = new SqlConnection(DBconnectionString))
            {
                connection.Open();
                DynamicParameters dynamicParameters = new();
                dynamicParameters.Add("@P_OffersAndPaymentsJson", claims);
                ResponseModelDB val = await connection.QueryFirstAsync<ResponseModelDB>(
                 nameof(StoredProceduresEnum.BK_SyncUpHistoricalClaims),
                 commandType: CommandType.StoredProcedure,
                 param: dynamicParameters,
                 commandTimeout: 100);

                //if(val != 1) {
                //    throw new Exception("Failed Updating");
                //}
                //connection.Execute(sql, parameters);
            }
        }
        catch (Exception)
        {
            throw;
        }
    }

    
static async Task SyncDataBaseEnvelope(Envelope envelope)
    {
        try { 
        
        using (var connection = new SqlConnection(DBconnectionString))
        {
            connection.Open();
            DynamicParameters dynamicParameters = new();
            dynamicParameters.Add("@P_EnvelopeID", envelope.EnvelopeId);
            dynamicParameters.Add("@P_Status", envelope.Status);
            dynamicParameters.Add("@P_StatusChangedDateTime", envelope.StatusChangedDateTime);
           int val = await connection.QueryFirstAsync<int>(
            nameof(StoredProceduresEnum.BK_UpdateEnvelopeStatusUsingConsoleJob),
            commandType: CommandType.StoredProcedure,
            param: dynamicParameters,
            commandTimeout: 100);

                //if(val != 1) {
                //    throw new Exception("Failed Updating");
                //}
            //connection.Execute(sql, parameters);
        }
        }
        catch (Exception)
        {
            throw;
        }
    }


    
    static async Task RunJObAndUploadDocumentsToNetDocuments()
{
    try
    {
            List<EnvelopeInformation> envelopeInformations = new List<EnvelopeInformation>();
            using (var connection = new SqlConnection(DBconnectionString))
        {
            connection.Open();
            DynamicParameters dynamicParameters = new();
                envelopeInformations = (List<EnvelopeInformation>)await connection.QueryAsync<EnvelopeInformation>(
             nameof(StoredProceduresEnum.BK_CompletedEnvelopesReadyForDocumentsUpload),
             commandType: CommandType.StoredProcedure,
             param: dynamicParameters,
             commandTimeout: 100);

            Console.WriteLine($"Total Envelopes Ready For Upload: {envelopeInformations.Count}");

                //connection.Execute(sql, parameters);
            }

            if(envelopeInformations.Count > 0)
            {
                foreach (var envelope in envelopeInformations)
                {
                    Console.WriteLine($"Envelope ID: {envelope.EnvelopeID}, Status: {envelope.EnvelopeStatus}");
                    // Here you can add code to upload documents to NetDocuments
                    // For example, using a hypothetical UploadToNetDocuments method
                    // await UploadToNetDocuments(envelope);

                    await GetCompletedDocumentsAsync(envelope.EnvelopeID ?? string.Empty);
                }
            }
        }
    catch (Exception)
    {
        throw;
    }
}

    public static async Task GetCompletedDocumentsAsync(string envelopeId)
    {
        try
        {
            
            IEnumerable<EnvelopeOfferClientInfo> envelopeOfferClientInfo = await GetEnvelopeOfferInfo(envelopeId);
            Console.WriteLine($"Envelopes COunt Is  {envelopeOfferClientInfo.Count()}");
            foreach (EnvelopeOfferClientInfo envelopeOfferInfo in envelopeOfferClientInfo) {
                DocusignService _docusignService = new DocusignService(DBconnectionString);
                var envelopeDocumentsResult = await _docusignService.GetCompletedDocumentAsync(envelopeId);
                if (envelopeDocumentsResult == null)
                {
                    return;
                }
                using var originalCopy = new MemoryStream();
                await envelopeDocumentsResult.CopyToAsync(originalCopy);
                originalCopy.Position = 0;
                Stream? streamCopy = new MemoryStream(originalCopy.ToArray());
                var currentDate = DateTime.Now.ToString("MMddyyyy");

                string UniqueDocId = await UploadReleaseToNetDocuments(streamCopy, envelopeOfferInfo.ClaimID ?? default(int), envelopeOfferInfo.TrustRefID ?? default(int), docName:$"SignedRelease_{envelopeOfferInfo.EquitracID}_{envelopeOfferInfo.TrustAbbr}_{envelopeOfferInfo.ClaimNumber}_{currentDate}");
                Console.WriteLine("***********************************************************************************************************************");
                Console.WriteLine(UniqueDocId);
                Console.WriteLine("***********************************************************************************************************************");
                ClaimMaster claimMaster = new ClaimMaster(DBconnectionString);
                await SaveReleaseSignedDocumentDataWithOffer(UniqueDocId, envelopeId);
                //var val =  await claimMaster.UploadDocumentTestAsync(envelopeOfferInfo.ClaimID ?? default(int), UniqueDocId, "Niranjan JOB");
                //await SaveReleaseSignedDocumentDataWithOfferToAggregator(val, envelopeId);

            }
            return;
        }
        catch (Exception)
        {
            return;
        }
    }
    
    //await this._clientRepository.SaveReleaseDocumentDataWithOffer(releaseDocumentRequiredInfo);
    
    public static async Task SaveReleaseSignedDocumentDataWithOffer(string netDocID, string EnvelopeID)
    {
        try
        {
            DynamicParameters dynamicParameters = new();
            dynamicParameters.Add("@P_EnvelopeID", EnvelopeID);
            dynamicParameters.Add("@P_SignedNetDocumentID", netDocID);

            using (var connection = new SqlConnection(DBconnectionString))
            {
                connection.Open();
                await connection.ExecuteAsync(
            nameof(StoredProceduresEnum.BK_UpdateEnvelopeWithSignedDocID),
            dynamicParameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 100);
                //connection.Execute(sql, parameters);
            }
        }
        catch (Exception)
        {
            throw;
        }
    }


    public static async Task SaveReleaseSignedDocumentDataWithOfferToAggregator(string aggDocID, string EnvelopeID)
    {
        try
        {
            DynamicParameters dynamicParameters = new();
            dynamicParameters.Add("@P_EnvelopeID", EnvelopeID);
            dynamicParameters.Add("@P_SignedAggDocumentID", aggDocID);

            using (var connection = new SqlConnection(DBconnectionString))
            {
                connection.Open();
                await connection.ExecuteAsync(
            nameof(StoredProceduresEnum.BK_UpdateEnvelopeWithSignedAggDocID),
            dynamicParameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 100);
                //connection.Execute(sql, parameters);
            }
        }
        catch (Exception)
        {
            throw;
        }
    }

    public static async Task<string> UploadReleaseToNetDocuments(Stream releaseDocument, int claimID, int trustRefID, string nDFolder = "Bankruptcy Release", string docName = "default")
    {
        var docService = new NetDocumentService(DBconnectionString);
        var uploadRequest = await GetNetDocumentMetadataAsync(claimID, trustRefID);
        using var tempMemoryStream = new MemoryStream();
        await releaseDocument.CopyToAsync(tempMemoryStream);

        tempMemoryStream.Position = 0;

        uploadRequest.UpdateDestination("Bankruptcy", nDFolder, "pdf", "NiranjanReddy", DocName: docName);
        var netDocStatus = await docService.UploadDocumentAsync(tempMemoryStream, uploadRequest);

        return netDocStatus?.DocumentId ?? string.Empty;
    }

    public static async Task<IEnumerable<EnvelopeOfferClientInfo>> GetEnvelopeOfferInfo(string EnvelopeID)
    {
        try
        {
            DynamicParameters dynamicParameters = new();
            dynamicParameters.Add("@P_EnvelopeID", EnvelopeID);

            IEnumerable<EnvelopeOfferClientInfo> doc = Enumerable.Empty<EnvelopeOfferClientInfo>();
            using (var connection = new SqlConnection(DBconnectionString))
            {
                connection.Open();
                doc = await connection.QueryAsync<EnvelopeOfferClientInfo>(
            nameof(StoredProceduresEnum.BK_OfferAndClaimInfoByEnvelopeId),
            dynamicParameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 100);
                //connection.Execute(sql, parameters);
            }




            return doc;
        }
        catch (Exception)
        {
            return Enumerable.Empty<EnvelopeOfferClientInfo>();
        }
    }
    public static async Task<UploadFileRequest> GetNetDocumentMetadataAsync(int claimId, int trustRefID)
    {
        try
        {
            DynamicParameters dynamicParameters = new();
            dynamicParameters.Add("@P_ClaimId", claimId);
            dynamicParameters.Add("@P_TrustRefID", trustRefID);
            IEnumerable<UploadFileRequest> doc = Enumerable.Empty<UploadFileRequest>();
            using (var connection = new SqlConnection(DBconnectionString))
            {
                connection.Open();
                doc = await connection.QueryAsync<UploadFileRequest>(
            nameof(StoredProceduresEnum.BK_GetNetDocumentMetadata),
            dynamicParameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 100);
                //connection.Execute(sql, parameters);
            }


           

            return doc?.FirstOrDefault() ?? new UploadFileRequest();
        }
        catch (Exception)
        {
            throw;
        }
    }


}