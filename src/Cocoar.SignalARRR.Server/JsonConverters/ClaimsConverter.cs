using System;
using System.Security.Claims;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cocoar.SignalARRR.Server.JsonConverters {
    public class ClaimsConverter : JsonConverter {
        public override bool CanConvert(Type objectType) {
            return (objectType == typeof(System.Security.Claims.Claim));
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
            var claim = (System.Security.Claims.Claim)value;
            JObject jo = new JObject();
            jo.Add("Type", claim.Type);
            jo.Add("Value", IsJson(claim.Value) ? new JRaw(claim.Value) : new JValue(claim.Value));
            jo.Add("ValueType", claim.ValueType);
            jo.Add("Issuer", claim.Issuer);
            jo.Add("OriginalIssuer", claim.OriginalIssuer);
            jo.WriteTo(writer);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
            JObject jo = JObject.Load(reader);
            string type = (string)jo["Type"] ?? (string)jo["type"];
            JToken token = jo["Value"] ?? jo["value"];

            string value = token.Type == JTokenType.String ? (string)token : token.ToString(Formatting.None);

            string valueType = (string)jo["ValueType"] ?? (string)jo["valueType"];
            string issuer = (string)jo["Issuer"] ?? (string)jo["issuer"];
            string originalIssuer = (string)jo["OriginalIssuer"] ?? (string)jo["originalIssuer"];
            return new Claim(type, value, valueType, issuer, originalIssuer);
        }

        private bool IsJson(string val) {
            return (val != null &&
                    (val.StartsWith("[") && val.EndsWith("]")) ||
                    (val.StartsWith("{") && val.EndsWith("}")));
        }
    }

}
