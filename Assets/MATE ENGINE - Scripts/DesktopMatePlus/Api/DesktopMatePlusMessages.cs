using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DesktopMatePlus
{
    // =========================================================================
    // Base
    // =========================================================================

    [Serializable]
    public class BaseMessage
    {
        public string type;
        public string id;
        public double? timestamp;
    }

    // =========================================================================
    // Client -> Server
    // =========================================================================

    [Serializable]
    public class AuthorizeMessage
    {
        public string type = "authorize";
        public string token;
    }

    [Serializable]
    public class PongMessage
    {
        public string type = "pong";
    }

    [Serializable]
    public class ImageUrl
    {
        public string url;
        public string detail = "auto";
    }

    [Serializable]
    public class ImageContent
    {
        public string type = "image_url";
        public ImageUrl image_url;
    }

    [Serializable]
    public class OutgoingChatMessage
    {
        public string type = "chat_message";
        public string content;
        public string agent_id;
        public string user_id;
        public string persona_id = "yuri";
        public string session_id;
        public bool tts_enabled = true;
        public string reference_id;
        public int limit = 10;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ImageContent[] images;
    }

    [Serializable]
    public class InterruptStreamMessage
    {
        public string type = "interrupt_stream";
        public string turn_id;
    }

    // =========================================================================
    // Server -> Client (parsed from JObject)
    // =========================================================================

    public class StreamStartData
    {
        public string turn_id;
        public string session_id;
    }

    public class StreamTokenData
    {
        public string chunk;
        public string node;
    }

    public class StreamEndData
    {
        public string turn_id;
        public string session_id;
        public string content;
    }

    public class TimelineKeyframe
    {
        public float duration;
        public Dictionary<string, float> targets = new();
    }

    public class TtsChunkData
    {
        public int sequence;
        public string text;
        public string audio_base64;
        public string emotion;
        public List<TimelineKeyframe> keyframes = new();
    }

    public class ErrorData
    {
        public string error;
        public int? code;
    }

    public class AuthorizeSuccessData
    {
        public string connection_id;
    }

    public class AuthorizeErrorData
    {
        public string error;
    }

    // =========================================================================
    // Parser
    // =========================================================================

    public static class MessageParser
    {
        public static string GetType(string json)
        {
            var obj = JObject.Parse(json);
            return obj["type"]?.ToString();
        }

        public static StreamStartData ParseStreamStart(JObject obj) => new()
        {
            turn_id = obj["turn_id"]?.ToString(),
            session_id = obj["session_id"]?.ToString()
        };

        public static StreamTokenData ParseStreamToken(JObject obj) => new()
        {
            chunk = obj["chunk"]?.ToString(),
            node = obj["node"]?.ToString()
        };

        public static StreamEndData ParseStreamEnd(JObject obj) => new()
        {
            turn_id = obj["turn_id"]?.ToString(),
            session_id = obj["session_id"]?.ToString(),
            content = obj["content"]?.ToString()
        };

        public static TtsChunkData ParseTtsChunk(JObject obj)
        {
            var data = new TtsChunkData
            {
                sequence = obj["sequence"]?.Value<int>() ?? 0,
                text = obj["text"]?.ToString(),
                audio_base64 = obj["audio_base64"]?.ToString(),
                emotion = obj["emotion"]?.ToString()
            };

            var kfArray = obj["keyframes"] as JArray;
            if (kfArray != null)
            {
                foreach (var kfToken in kfArray)
                {
                    var kf = new TimelineKeyframe
                    {
                        duration = kfToken["duration"]?.Value<float>() ?? 0f
                    };
                    var targets = kfToken["targets"] as JObject;
                    if (targets != null)
                    {
                        foreach (var prop in targets.Properties())
                        {
                            kf.targets[prop.Name] = prop.Value.Value<float>();
                        }
                    }
                    data.keyframes.Add(kf);
                }
            }
            return data;
        }

        public static ErrorData ParseError(JObject obj) => new()
        {
            error = obj["error"]?.ToString(),
            code = obj["code"]?.Value<int>()
        };

        public static AuthorizeSuccessData ParseAuthorizeSuccess(JObject obj) => new()
        {
            connection_id = obj["connection_id"]?.ToString()
        };

        public static AuthorizeErrorData ParseAuthorizeError(JObject obj) => new()
        {
            error = obj["error"]?.ToString()
        };

        /// <summary>
        /// Serialize a client message to JSON.
        /// </summary>
        public static string Serialize(object msg) => JsonConvert.SerializeObject(msg);
    }
}
