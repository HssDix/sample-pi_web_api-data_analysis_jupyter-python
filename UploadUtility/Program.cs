﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UploadUtility
{
    public static class Program
    {
        private const string DefaultConfigFile = "test_config.json";
        private const string DefaultDatabaseFile = "Building Example.xml";
        private const string DefaultTagDefinitionFile = "tagdefinition.csv";
        private const string DefaultPIDataFile = "pidata.csv";

        private static JObject _config;
        private static PIWebAPIClient _client;

        public static void Main(string[] args)
        {
            /*Use the default values provided at the beginning of this class (which work when running from Visual Studio) 
                or use the values provided by command line arguments*/

            string configFile = DefaultConfigFile;
            string databaseFile = DefaultDatabaseFile;
            string tagDefinitionFile = DefaultTagDefinitionFile;
            string piDataFile = DefaultPIDataFile;

            if (args != null)
            {
                if (args.Length >= 1)
                {
                    databaseFile = args[0];
                }

                if (args.Length >= 2)
                {
                    tagDefinitionFile = args[1];
                }

                if (args.Length >= 3)
                {
                    piDataFile = args[2];
                }

                if (args.Length >= 4)
                {
                    configFile = args[3];
                }
            }

            _config = JObject.Parse(File.ReadAllText(configFile));
            _client = new PIWebAPIClient(
                _config["Resource"].ToString(),
                _config["Username"].ToString(),
                _config["Password"].ToString());

            string dataserver = _config["DataServerName"].ToString();
            string assetserver = _config["AssetServerName"].ToString();

            // Delete existing AF Database if it exists
            if (DoesDatabaseExist(assetserver))
            {
                DeleteExistingDatabase(assetserver);
            }

            // Create and Import Database from Building Example file
            var doc = new XmlDocument();
            doc.Load(databaseFile);
            CreateDatabase(doc, assetserver);

            // Check for and create tags
            if (!DoesTagExist(dataserver))
            {
                CreatePIPoint(dataserver, tagDefinitionFile);
            }

            // Update values from existing csv file
            UpdateValues(dataserver, tagDefinitionFile, piDataFile);
        }

        private static string GetWebIDByPath(string path, string resource)
        {
            string query = $"{resource}?path={path}";

            JObject response = _client.GetRequest(query);
                     
            return response["WebId"].ToString();
        }

        private static void CreateDatabase(XmlDocument doc, string assetserver)
        {
            string serverPath = $"\\\\{assetserver}";
            string assetserverWebID = GetWebIDByPath(serverPath, "assetservers");

            string createDBQuery = $"assetservers/{assetserverWebID}/assetdatabases";

            string databaseName = _config["AssetDatabaseName"].ToString();

            object payload = new
            {
                Name = databaseName,
                Description = "Example for Building Data",
            };

            string request_body = JsonConvert.SerializeObject(payload);

            _client.PostRequest(createDBQuery, request_body);

            string databasePath = $"{serverPath}\\{databaseName}";
            string databaseWebID = GetWebIDByPath(databasePath, "assetdatabases");
            string importQuery = $"assetdatabases/{databaseWebID}/import";

            _client.PostRequest(importQuery, doc.InnerXml.ToString(), true);
        }

        private static void CreatePIPoint(string dataserver, string tagDefinitionLocation)
        {
            string path = $"\\\\PIServers[{dataserver}]";
            string dataserverWebID = GetWebIDByPath(path, "dataservers");
            string createPIPointQuery = $"dataservers/{dataserverWebID}/points";

            var tagDefinitions = File.ReadLines(tagDefinitionLocation);
            string name, pointType, pointClass;

            foreach (string tagDefinition in tagDefinitions)
            {
                string[] split = tagDefinition.Split(',');
                name = split[0];
                pointType = split[1];
                pointClass = split[2];

                object payload = new
                {
                    Name = name,
                    PointType = pointType,
                    PointClass = pointClass,
                };

                string request_body = JsonConvert.SerializeObject(payload);

                _client.PostRequest(createPIPointQuery, request_body);
            }
        }

        private static void DeleteExistingDatabase(string assetserver)
        {
            string databaseName = _config["AssetDatabaseName"].ToString();
            string databasePath = $"\\\\{assetserver}\\{databaseName}";
            string databaseWebID = GetWebIDByPath(databasePath, "assetdatabases");

            string deleteQuery = $"assetdatabases/{databaseWebID}";
            _client.DeleteRequest(deleteQuery);
        }

        private static bool DoesDatabaseExist(string assetserver)
        {
            string databaseName = _config["AssetDatabaseName"].ToString();
            string databasePath = $"\\\\{assetserver}\\{databaseName}";

            string getDatabaseQuery = $"assetdatabases/?path={databasePath}";

            try
            {
                JObject result = _client.GetRequest(getDatabaseQuery);
            }
            catch (Exception e)
            {
                if (e.InnerException?.Message != null && e.InnerException.Message.Contains("404"))
                {
                    return false;
                }

                throw;
            }

            return true;
        }

        private static bool DoesTagExist(string dataserver)
        {
            string tagname = "VAVCO 2-09.Predicted Cooling Time";

            string path = $"\\\\{dataserver}\\{tagname}";
            string getPointQuery = $"points?path={path}";

            try
            {
                JObject result = _client.GetRequest(getPointQuery);
            }
            catch (Exception e)
            {
                if (e.InnerException?.Message != null && e.InnerException.Message.Contains("404"))
                {
                    return false;
                }

                throw;
            }

            return true;
        }

        private static void UpdateValues(string dataserver, string tagDefinitionLocation, string piDataLocation)
        {
            var tags = File.ReadLines(tagDefinitionLocation);

            foreach (string tag in tags)
            {
                string[] split = tag.Split(',');
                string tagname = split[0];
                var entries = new List<string[]>();

                var values = File.ReadLines(piDataLocation);
                foreach (string value in values)
                {
                    if (value.Contains(tagname))
                    {
                        entries.Add(value.Split(','));
                    }
                }

                string path = $"\\\\{dataserver}\\{tagname}";
                string webid = GetWebIDByPath(path, "points");
                string updateValueQuery = "streamsets/recorded";

                var items = new List<object>();
                foreach (string[] line in entries)
                {
                    object item = new
                    {
                        Timestamp = line[3],
                        Value = line[1],
                    };
                    items.Add(item);
                }

                object payload = new
                {
                    Items = items.ToArray(),
                    WebId = webid,
                };

                string request_body = "[" + JsonConvert.SerializeObject(payload) + "]";
                _client.PostRequest(updateValueQuery, request_body);
            }
        }
    }
}
