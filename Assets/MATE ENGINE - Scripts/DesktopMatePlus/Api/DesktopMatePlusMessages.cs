using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DesktopMatePlus
{
    // =========================================================================
    // Client -> Server (outbound frames)
    //
    // Mirrors the Pydantic models in nanobot_runtime's desktop_mate_protocol.py.
    // Inbound validation on the server uses a discriminated union on ``type``.
    // ``content`` must be non-empty. Extra fields are ignored — safe to add
    // more later without breaking the server. ``images`` mirrors the optional
    // ``images: list[str]`` field on the server-side models: each entry is a
    // full ``data:image/png;base64,...`` data URL the server will decode via
    // ``save_base64_data_url``.
    // =========================================================================

    [Serializable]
    public class NewChatMessage
    {
        public string type = "new_chat";
        public string content;
        public bool tts_enabled = true;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string reference_id;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string[] images;
    }

    [Serializable]
    public class ChatMessage
    {
        public string type = "message";
        public string chat_id;
        public string content;
        public bool tts_enabled = true;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string reference_id;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string[] images;
    }

    // =========================================================================
    // Server -> Client (inbound events)
    //
    // The server discriminates with ``event`` (not ``type``). Every event
    // except ``ready`` carries a ``chat_id``. ``proactive`` is optional and
    // true only for server-initiated (idle / cron) turns.
    // =========================================================================

    public class ReadyData
    {
        public string connection_id;
        public string client_id;
        public double server_time;
    }

    public class StreamStartData
    {
        public string chat_id;
        public bool proactive;
    }

    public class DeltaData
    {
        public string chat_id;
        public string text;
        public string stream_id;
        public bool proactive;
    }

    public class StreamEndData
    {
        public string chat_id;
        public string content;
        public bool proactive;
    }

    public class TimelineKeyframe
    {
        public float duration;
        public Dictionary<string, float> targets = new();
    }

    public class TtsChunkData
    {
        public string chat_id;
        public int sequence;
        public string text;
        // audio_base64 and emotion are "explicit-null-significant" — the server
        // keeps the keys with null values to mean "synthesis failed, play
        // silence". Downstream code should treat null as a valid, non-error
        // outcome.
        public string audio_base64;
        public string emotion;
        public List<TimelineKeyframe> keyframes = new();
        public bool proactive;
    }

    // =========================================================================
    // Parser
    // =========================================================================

    public static class MessageParser
    {
        /// <summary>Return the inbound envelope's ``event`` field, or null for non-JSON frames.</summary>
        public static string GetEvent(string json)
        {
            var obj = JObject.Parse(json);
            return obj["event"]?.ToString();
        }

        public static ReadyData ParseReady(JObject obj) => new()
        {
            connection_id = obj["connection_id"]?.ToString(),
            client_id = obj["client_id"]?.ToString(),
            server_time = obj["server_time"]?.Value<double>() ?? 0.0,
        };

        public static StreamStartData ParseStreamStart(JObject obj) => new()
        {
            chat_id = obj["chat_id"]?.ToString(),
            proactive = obj["proactive"]?.Value<bool>() ?? false,
        };

        public static DeltaData ParseDelta(JObject obj) => new()
        {
            chat_id = obj["chat_id"]?.ToString(),
            text = obj["text"]?.ToString(),
            stream_id = obj["stream_id"]?.ToString(),
            proactive = obj["proactive"]?.Value<bool>() ?? false,
        };

        public static StreamEndData ParseStreamEnd(JObject obj) => new()
        {
            chat_id = obj["chat_id"]?.ToString(),
            content = obj["content"]?.ToString(),
            proactive = obj["proactive"]?.Value<bool>() ?? false,
        };

        public static TtsChunkData ParseTtsChunk(JObject obj)
        {
            var data = new TtsChunkData
            {
                chat_id = obj["chat_id"]?.ToString(),
                sequence = obj["sequence"]?.Value<int>() ?? 0,
                text = obj["text"]?.ToString(),
                // Preserve explicit-null: .ToString() on a JToken whose Type is
                // Null returns "", which we treat the same as null downstream.
                audio_base64 = obj["audio_base64"]?.Type == JTokenType.Null
                    ? null
                    : obj["audio_base64"]?.ToString(),
                emotion = obj["emotion"]?.Type == JTokenType.Null
                    ? null
                    : obj["emotion"]?.ToString(),
                proactive = obj["proactive"]?.Value<bool>() ?? false,
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

        /// <summary>Serialize a client message to JSON.</summary>
        public static string Serialize(object msg) => JsonConvert.SerializeObject(msg);
    }
}
