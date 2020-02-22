namespace ProtobufTcpHelpers.Sample.Models
{
    using Newtonsoft.Json;

    public class WrapperJson
    {
        [JsonProperty]
        public string Operation { get; set; }

        [JsonProperty]
        public string Body { get; set; }

        protected internal WrapperJson()
        {

        }

        public WrapperJson(string operation, object content = null)
        {
            Operation = operation;

            if (content != null)
            {
                Body = JsonConvert.SerializeObject(content);
            }
        }
    }
}
