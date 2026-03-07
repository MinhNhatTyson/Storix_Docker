namespace Storix_BE.Service.Interfaces
{
    public static class PaymentExceptionCodes
    {
        public const string PaymentRequired = "PAY-EX-01";
        public const string ViewOnlyAccess = "PAY-EX-02";
        public const string PaymentPending = "PAY-EX-03";
        public const string PaymentFailed = "PAY-EX-04";
        public const string DuplicateSuccessPayment = "PAY-EX-05";
        public const string InvalidPaymentStatus = "PAY-EX-06";
        public const string InvalidPaymentUpdate = "PAY-EX-07";
        public const string CompanyNotFound = "PAY-EX-08";
        public const string CompanyInactive = "PAY-EX-09";
        public const string PaymentProviderError = "PAY-EX-10";
        public const string PaymentCallbackMismatch = "PAY-EX-11";
        public const string PaymentCheckFailed = "PAY-EX-12";
        public const string CrossCompanyAccessDenied = "PAY-EX-13";
    }
}
