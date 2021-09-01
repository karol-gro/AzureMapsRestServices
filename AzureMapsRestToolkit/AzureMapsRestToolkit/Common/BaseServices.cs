﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Azure.Core.GeoJson;

namespace AzureMapsToolkit.Common
{
    public class BaseServices
    {
        internal string Key { get; set; }

        public BaseServices(string key)
        {
            Key = key;
        }

        internal static async Task<HttpResponseMessage> GetHttpResponseMessage(string url, string data, HttpMethod method)
        {

            using var client = new HttpClient();
            client.BaseAddress = new Uri(url);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            using var request = new HttpRequestMessage(method, url);
            var content = new StringContent(data); //, Encoding.UTF8, "application/json"))

            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Content = content;
            var response = await client.SendAsync(request); //, HttpCompletionOption.ResponseHeadersRead);

            response.EnsureSuccessStatusCode();
            return response;
        }

        string _url = string.Empty;
        internal string Url
        {
            get
            {
                return _url.Equals(string.Empty) ?
                 $"?subscription-key={Key}" : _url;
            }
            set
            {
                _url = value;
            }
        }


        internal static GeoPointCollection GetMultiPoint(IEnumerable<Coordinate> coordinates)
        {
            List<GeoPoint> points = new(coordinates.Count());

            //double[,] points = new double[coordinates.Count(), 2];
            for (int i = 0; i < coordinates.Count(); i++)
            {
                GeoPoint point = new(coordinates.ElementAt(i).Longitude, coordinates.ElementAt(i).Latitude);
                points.Add(point);
            }

            GeoPointCollection multiPoint = new GeoPointCollection(points);
          
            return multiPoint;

        }


        internal IEnumerable<string> GetSearchQuery<T>(IEnumerable<T> req) where T : RequestBase
        {
            foreach (var reqItem in req)
            {
                var query = GetQuery(reqItem, false, false, '?');
                yield return query;
            }
        }

        internal string GetQuery<T>(T request, bool includeApiVersion, bool includeLanguage = true, char firstChar = '&', bool toCamelCase = true)
            where T : RequestBase
        {
            Type type = request.GetType();

            var properties = type.GetProperties();

            var arguments = string.Empty;

            foreach (var propertyInfo in properties)
            {
                if (propertyInfo.GetValue(request) != null)
                {
                    var argumentName = string.Empty;
                    var argumentValue = string.Empty;

                    var underLayingtype = Nullable.GetUnderlyingType(propertyInfo.PropertyType);

                    if (propertyInfo?.PropertyType.IsEnum == true || underLayingtype?.IsEnum == true)
                    {
                        argumentName = Char.ToLower(propertyInfo.Name[0]) + propertyInfo.Name[1..];
                        argumentValue = propertyInfo.GetValue(request).ToString().Replace(" ", "");

                        if (toCamelCase)
                            argumentValue = argumentValue.ToCamelCase();
                    }
                    //else if (propertyInfo != null && propertyInfo.PropertyType.IsArray)
                    //{
                    //    argumentName = Char.ToLower(propertyInfo.Name[0]) + propertyInfo.Name.Substring(1)
                    //}
                    else
                    {

                        var nameAttribute = propertyInfo.GetCustomAttributes(typeof(NameArgument), false);
                        if (nameAttribute.Length > 0)
                            argumentName = ((NameArgument)nameAttribute[0]).Name;

                        else
                        {
                            var jsonAttribute = propertyInfo.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false);
                            if (jsonAttribute.Length > 0)
                            {
                                argumentName = ((JsonPropertyNameAttribute)jsonAttribute[0]).Name;
                            }
                        }

                        if (argumentName == string.Empty)
                            argumentName = Char.ToLower(propertyInfo.Name[0]) + propertyInfo.Name[1..];


                        if (string.IsNullOrEmpty(argumentValue))
                        {
                            if (propertyInfo.PropertyType.IsArray)
                            {
                                object[] array = (object[])propertyInfo.GetValue(request);
                                argumentValue = string.Join(",", array);
                            }
                            else
                            {
                                argumentValue = propertyInfo.GetValue(request).ToString();

                                if (propertyInfo.PropertyType == typeof(int) && int.TryParse(argumentValue, out int i))
                                {
                                    argumentValue = ((int)propertyInfo.GetValue(request)).ToString(CultureInfo.InvariantCulture);
                                }

                                if (propertyInfo.PropertyType == typeof(double) || propertyInfo.PropertyType == typeof(double?)
                                    && double.TryParse(argumentValue, out double d))
                                {
                                    argumentValue = ((double)propertyInfo.GetValue(request)).ToString(CultureInfo.InvariantCulture);
                                }
                            }
                        }
                    }

                    arguments += $"{firstChar}{argumentName}={argumentValue}";
                    firstChar = '&';
                }
            }
            return arguments;
        }




        internal static async Task<byte[]> GetByteArray(string baseAddress, string url)
        {
            using var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(baseAddress);
            var content = await httpClient.GetByteArrayAsync(url);
            return content;
        }

        internal async Task<Response<T>> ExecuteRequest<T, U>(string baseUrl, U req, bool toCamelCase = true) where U : RequestBase
        {
            Url = string.Empty;
            try
            {
                if (req != null)
                    Url += GetQuery<U>(req, true, toCamelCase: toCamelCase);

                using HttpClient client = GetClient(baseUrl);
                T data = await GetData<T>(client, Url);
                Response<T> response = GetResponse(data);
                return response;
            }
            catch (AzureMapsException ex)
            {
                return Response<T>.CreateErrorResponse(ex);
            }
        }

        internal static string GetQuerycontent(IEnumerable<string> queryCollection)
        {
            Batch b = new();

            b.BatchItems = new List<BatchItem>(queryCollection.Count());
            foreach (string item in queryCollection)
            {
                BatchItem bi = new ();
                bi.Query = item;
                b.BatchItems.Add(bi);
            }

            string queryContent = JsonSerializer.Serialize<Batch>(b);
            return queryContent;
        }

        internal static HttpClient GetClient(string baseAddress)
        {
            var client = new HttpClient();
            if (!string.IsNullOrEmpty(baseAddress))
                client.BaseAddress = new Uri(baseAddress);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        internal static Response<T> GetResponse<T>(T t)
        {
            var res = new Response<T>
            {
                Result = t
            };
            return res;
        }

        internal static async Task<T> GetData<T>(HttpClient client, string url)
        {
            var res = await client.GetAsync(url);
            if (res.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var json = await res.Content.ReadAsStringAsync();
                var val = JsonSerializer.Deserialize<T>(json);
                return val;
            }
            else
            {
                string content = await res.Content.ReadAsStringAsync();

                var ex = JsonSerializer.Deserialize<ErrorResponse>(content);

                throw new AzureMapsException(ex);
            }

        }

        internal static async Task<string> GetUdidFromLocation(string location)
        {
            using (var client = GetClient(location))
            {
                var tries = 0;
                while (tries < 20)
                {
                    var result = await client.GetAsync(location);
                    if (result.StatusCode == System.Net.HttpStatusCode.Created)
                    {
                        var data = await result.Content.ReadAsStringAsync();
                        return data;
                    }
                    else if (result.StatusCode == System.Net.HttpStatusCode.Accepted)
                    {
                        await Task.Delay(100);
                    }
                    else
                    {
                        throw new Exception($"Error, the document location is {location}");
                    }
                    tries += 1;
                }
            }
            throw new Exception($"Error, the document location is {location}");
        }

    }
}
