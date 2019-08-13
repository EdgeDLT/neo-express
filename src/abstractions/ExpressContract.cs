﻿using System.Collections.Generic;
using Newtonsoft.Json;

namespace NeoExpress.Abstractions
{
    public class ExpressContract
    {
        public class Parameter
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("type")]
            public string Type { get; set; }
        }

        public class Function
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("returntype")]
            public string ReturnType { get; set; }

            [JsonProperty("parameters")]
            public List<Parameter> Parameters { get; set; }
        }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("hash")]
        public string Hash { get; set; }

        [JsonProperty("entrypoint")]
        public string Entrypoint { get; set; }

        [JsonProperty("contract-data")]
        public string ContractData { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }

        // TODO: ContractPropertyState

        [JsonProperty("functions")]
        public List<Function> Functions { get; set; }

        [JsonProperty("events")]
        public List<Function> Events { get; set; }
    }
}
