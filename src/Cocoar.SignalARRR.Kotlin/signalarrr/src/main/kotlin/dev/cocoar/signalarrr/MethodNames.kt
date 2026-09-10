package dev.cocoar.signalarrr

/**
 * The SignalR hub method names of the SignalARRR wire protocol.
 *
 * Mirrors `Cocoar.SignalARRR.Common.Constants.MethodNames` on the server.
 */
public object MethodNames {
    // Client → Server
    public const val INVOKE_MESSAGE: String = "InvokeMessage"
    public const val INVOKE_MESSAGE_RESULT: String = "InvokeMessageResult"
    public const val SEND_MESSAGE: String = "SendMessage"
    public const val STREAM_MESSAGE: String = "StreamMessage"

    // Server → Client
    public const val INVOKE_SERVER_REQUEST: String = "InvokeServerRequest"
    public const val INVOKE_SERVER_MESSAGE: String = "InvokeServerMessage"
    public const val CHALLENGE_AUTHENTICATION: String = "ChallengeAuthentication"
    public const val CANCEL_TOKEN_FROM_SERVER: String = "CancelTokenFromServer"

    // Client → Server item streaming
    public const val STREAM_ITEM_TO_SERVER: String = "StreamItemToServer"
    public const val STREAM_COMPLETE_TO_SERVER: String = "StreamCompleteToServer"

    /** Bare hub method that mints a one-time HTTP upload URL. */
    public const val REQUEST_UPLOAD_SLOT: String = "RequestUploadSlot"
}
