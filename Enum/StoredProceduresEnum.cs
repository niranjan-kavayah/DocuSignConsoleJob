using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocuSignConsoleApp.Enum
{
    public enum StoredProceduresEnum
    {
        [Description("Update Envelope Status")]
        BK_UpdateEnvelopeStatusUsingConsoleJob,

        [Description("Get Completed Documents Rady For DOcuments Upload")]
        BK_CompletedEnvelopesReadyForDocumentsUpload,

        [Description("Get Net Documents Meta Data")]
        BK_GetNetDocumentMetadata,

        [Description("Get Offer and Claim & Client Info By EnvelopeID ")]
        BK_OfferAndClaimInfoByEnvelopeId,

        [Description("Update Envelope Info By EnvelopeID ")]
        BK_UpdateEnvelopeWithSignedDocID,

        [Description("Associate Net Document Upload ID to Envelope ID ")]
        BK_UpdateEnvelopeWithSignedAggDocID,

        BK_SyncUpHistoricalClaims
    }
}
