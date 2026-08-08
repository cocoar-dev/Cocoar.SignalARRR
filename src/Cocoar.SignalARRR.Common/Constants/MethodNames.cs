namespace Cocoar.SignalARRR.Common.Constants {

    /// <summary>
    /// The SignalR method names SignalARRR sends and listens on. They are the wire protocol, so they
    /// are constants — two of them used to have public setters, which let any consumer rewrite the
    /// protocol process-wide at runtime and leave every other client on the connection talking a
    /// name nothing was listening for.
    /// </summary>
    public static class MethodNames {
        public const string InvokeMessageOnServer = "InvokeMessage";
        public const string InvokeMessageResultOnServer = "InvokeMessageResult";
        public const string SendMessageToServer = "SendMessage";
        public const string StreamMessageFromServer = "StreamMessage";

        public const string InvokeServerRequest = "InvokeServerRequest";

        public const string ChallengeAuthentication = "ChallengeAuthentication";
        public const string InvokeServerMessage = "InvokeServerMessage";

        public const string CancelTokenFromServer = "CancelTokenFromServer";

        public const string StreamItemToServer = "StreamItemToServer";
        public const string StreamCompleteToServer = "StreamCompleteToServer";
    }
}
