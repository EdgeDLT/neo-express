﻿using System.Collections.Generic;
using Newtonsoft.Json;

namespace Neo3Express.Models
{
    public class AbiContract
    {
        public class Parameter
        {
            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("type")]
            public string Type { get; set; } = string.Empty;
        }

        public class Function
        {
            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("parameters")]
            public List<Parameter> Parameters { get; set; } = new List<Parameter>();

            [JsonProperty("returntype")]
            public string ReturnType { get; set; } = string.Empty;
        }

        [JsonProperty("hash")]
        public string Hash { get; set; } = string.Empty;

        [JsonProperty("entrypoint")]
        public Function Entrypoint { get; set; } = new Function();

        [JsonProperty("functions")]
        public List<Function> Functions { get; set; } = new List<Function>();

        [JsonProperty("events")]
        public List<Function> Events { get; set; } = new List<Function>();
    }
}
