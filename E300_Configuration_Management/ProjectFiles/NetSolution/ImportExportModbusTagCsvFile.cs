#region Using directives

using FTOptix.CommunicationDriver;
using FTOptix.Core;
using FTOptix.HMIProject;
using FTOptix.Modbus;
using FTOptix.NetLogic;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;

#endregion

public class ImportExportModbusTagCsvFile : BaseNetLogic
{
    [ExportMethod]
    public void ExportTags()
    {
        // Create long running task to handle export
        exportTagsTask?.Dispose();
        exportTagsTask = new LongRunningTask(ExportTagsMethod, LogicObject);
        exportTagsTask.Start();
    }

    private void ExportTagsMethod()
    {
        if (!CheckParameters())
            return;

        var exportedTags = GenerateCsv(GetTagsFromRootRecursively(ClearPathFromProjectInfo(Log.Node(myDriver))));
        Log.Info(MethodBase.GetCurrentMethod().Name, $"Finished exporting {exportedTags} tag(s) to CSV file");
    }

    [ExportMethod]
    public void ImportTags()
    {
        // Create long running task to handle import
        importTagsTask?.Dispose();
        importTagsTask = new LongRunningTask(ImportTagsMethod, LogicObject);
        importTagsTask.Start();
    }

    private void ImportTagsMethod()
    {
        if (!CheckParameters())
            return;

        var importedTags = ImportFromCsv(csvFilePath);
        Log.Info(MethodBase.GetCurrentMethod().Name, $"Successfully imported {importedTags} tag(s) from CSV file");
    }

    private int ImportFromCsv(string csvPath)
    {
        Log.Info(MethodBase.GetCurrentMethod().Name, $"Importing tag(s) from CSV file at: \"{csvPath}\"");
        var importedTags = 0;

        try
        {
            using var reader = new StreamReader(csvPath);
            var csvTagObjects = new List<CsvTagObject>();

            // Read CSV header
            var headerColumns = reader.ReadLine()?.Split(fieldSeparator).ToList();
            if (headerColumns == null)
            {
                Log.Error(MethodBase.GetCurrentMethod().Name, "CSV file is empty or has no header.");
                return 0;
            }

            // Initialize flag to detect text wrapping
            bool wrapFieldsDetected = false;

            // Read CSV data and create custom class
            while (!reader.EndOfStream)
            {
                var rowData = reader.ReadLine()?.Split(fieldSeparator).ToList();
                if (rowData == null)
                    continue;

                var obj = GetDataFromCsvRow(rowData, headerColumns, ref wrapFieldsDetected);
                if (obj != null)
                {
                    csvTagObjects.Add(obj);
                }
            }

            // Process imported data
            foreach (var item in csvTagObjects)
            {
                AddModbusTag(item);
                importedTags++;
            }
        }
        catch (Exception e)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, e.Message);
        }

        return importedTags;
    }

    private void AddModbusTag(CsvTagObject tagObject)
    {
        // Retrieve tag parameters from the CsvTagObject
        var tagParameters = GetTagParameters(tagObject);

        // Create and add the tag to the project
        CreateAndAddTag(tagParameters);
    }

    private TagParameters GetTagParameters(CsvTagObject tagObject)
    {
        // Retrieve tag parameters
        var tagPath = GetValueFromDictionary(tagObject.Variables, "Path");
        var tagName = GetValueFromDictionary(tagObject.Variables, "VariableName");
        var type = GetValueFromDictionary(tagObject.Variables, "Type");
        var isStructure = bool.Parse(GetValueFromDictionary(tagObject.Variables, "IsStructure"));
        var arrayElements = uint.Parse(GetValueFromDictionary(tagObject.Variables, "ArrayElements"));
        var arrayUpdateMode = GetValueFromDictionary(tagObject.Variables, "ArrayUpdateMode");
        var BitOffset = uint.Parse(GetValueFromDictionary(tagObject.Variables, "BitOffset"));
        var numCoil = ushort.Parse(GetValueFromDictionary(tagObject.Variables, "NumCoil"));
        var numDiscreteInput = ushort.Parse(GetValueFromDictionary(tagObject.Variables, "NumDiscreteInput"));
        var maximumLength = int.Parse(GetValueFromDictionary(tagObject.Variables, "MaximumLength"));
        var memoryArea = GetValueFromDictionary(tagObject.Variables, "MemoryArea");
        var registerNumber = ushort.Parse(GetValueFromDictionary(tagObject.Variables, "RegisterNumber"));

        // Determine if it's the first structure element
        var isFirstStructureElement = DetermineFirstStructureElement(isStructure, tagPath);

        // Create and return TagParameters object
        return new TagParameters(tagPath, tagName, type, isStructure, arrayElements, arrayUpdateMode, BitOffset,
                                 numCoil, numDiscreteInput, maximumLength, memoryArea, registerNumber, isFirstStructureElement);
    }

    private bool DetermineFirstStructureElement(bool isStructure, string tagPath)
    {
        if (!isStructure)
            return false;

        // Split the tag path to get the structure name
        var tempPath = tagPath.Split('/');
        var structureName = tempPath.LastOrDefault();

        // Check if it's the first element of the structure
        var isFirstStructureElement = structureName != lastStructure;
        lastStructure = structureName;

        return isFirstStructureElement;
    }

    private static void CreateAndAddTag(TagParameters tagParameters)
    {
        // Create Folder for the tag if not existing
        if (!CreateFoldersTreeFromPath(tagParameters.TagPath))
            return;

        // Delete the tag if it already exists
        var existingTag = Project.Current.Get($"{tagParameters.TagPath}/{tagParameters.TagName}");
        existingTag?.Delete();

        // Create the tag
        var modbusTag = CreateModbusTag(tagParameters);

        // Add the tag to the project
        AddTagToProject(tagParameters, modbusTag);
    }

    private static FTOptix.Modbus.Tag CreateModbusTag(TagParameters tagParameters)
    {
        // Create Modbus tag based on type
        var typeNodeId = GetTypeNodeId(tagParameters.Type);
        var modbusTag = InformationModel.MakeVariable<FTOptix.Modbus.Tag>(tagParameters.TagName, typeNodeId);

        // Set tag parameters
        modbusTag.BitOffset = tagParameters.BitOffset;
        modbusTag.NumCoil = tagParameters.NumCoil;
        modbusTag.NumDiscreteInput = tagParameters.NumDiscreteInput;
        modbusTag.MaximumLength = tagParameters.MaximumLength;
        modbusTag.NumRegister = tagParameters.RegisterNumber;

        // Set array parameters
        if (tagParameters.ArrayElements > 0)
        {
            var arrayDimensions = new uint[1];
            arrayDimensions[0] = tagParameters.ArrayElements;
            modbusTag.ArrayDimensions = new uint[] { tagParameters.ArrayElements };
        }

        // Set array update mode
        modbusTag.ArrayUpdateMode = tagParameters.ArrayUpdateMode == "Element" ?
                                    TagArrayUpdateMode.Element : TagArrayUpdateMode.Array;

        // Set memory area
        modbusTag.MemoryArea = GetMemoryArea(tagParameters.MemoryArea);

        return modbusTag;
    }

    private static NodeId GetTypeNodeId(string type)
    {
        // Map type to NodeId
        return type switch
        {
            "Boolean" => OpcUa.DataTypes.Boolean,
            "Int16" => OpcUa.DataTypes.Int16,
            "Int32" => OpcUa.DataTypes.Int32,
            "Int64" => OpcUa.DataTypes.Int64,
            "UInt16" => OpcUa.DataTypes.UInt16,
            "UInt32" => OpcUa.DataTypes.UInt32,
            "UInt64" => OpcUa.DataTypes.UInt64,
            "Float" => OpcUa.DataTypes.Float,
            "Double" => OpcUa.DataTypes.Double,
            "String" => OpcUa.DataTypes.String,
            "Byte" => OpcUa.DataTypes.Byte,
            "SByte" => OpcUa.DataTypes.SByte,
            _ => throw new ArgumentException($"DataType \"{type}\" is not supported by this script"),
        };
    }

    private static ModbusMemoryArea GetMemoryArea(string memoryArea)
    {
        // Map memory area to ModbusMemoryArea
        return memoryArea switch
        {
            "HoldingRegister" => ModbusMemoryArea.HoldingRegister,
            "Coil" => ModbusMemoryArea.Coil,
            "InputRegister" => ModbusMemoryArea.InputRegister,
            "DiscreteInput" => ModbusMemoryArea.DiscreteInput,
            _ => throw new ArgumentException($"{memoryArea} cannot be mapped to any valid memory area"),
        };
    }

    private static void AddTagToProject(TagParameters tagParameters, FTOptix.Modbus.Tag modbusTag)
    {
        if (tagParameters.IsStructure)
        {
            var structureNode = Project.Current.Get<TagStructure>($"{tagParameters.TagPath}/{tagParameters.TagName}");
            if (tagParameters.IsFirstStructureElement)
            {
                structureNode?.Delete();
                structureNode = InformationModel.Make<FTOptix.CommunicationDriver.TagStructure>(tagParameters.TagName);
                structureNode.Add(modbusTag);
                Project.Current.Get(tagParameters.TagPath).Add(structureNode);
            }
            else
            {
                structureNode?.Add(modbusTag);
            }
        }
        else
        {
            Project.Current.Get(tagParameters.TagPath).Add(modbusTag);
        }
    }

    private static bool CreateFoldersTreeFromPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return true;

        var segments = path.Split('/').ToList();
        string updatedSegment = "";
        string segmentsAccumulator = "";

        try
        {
            foreach (var segment in segments)
            {
                if (segmentsAccumulator.Length == 0)
                    updatedSegment = segment;
                else
                    updatedSegment = $"{updatedSegment}/{segment}";
                var folder = InformationModel.MakeObject<Folder>(segment);
                var folderAlreadyExists = Project.Current.Get(updatedSegment) != null;
                if (!folderAlreadyExists)
                {
                    if (segmentsAccumulator.Length == 0)
                        Project.Current.Add(folder);
                    else
                        Project.Current.Get(segmentsAccumulator).Children.Add(folder);
                }
                segmentsAccumulator = updatedSegment;
            }
        }
        catch (Exception e)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, $"Cannot create Folder object in current project, error {e.Message}");
            return false;
        }
        return true;
    }

    private bool CheckParameters()
    {
        // Check CommDriver
        myDriver = InformationModel.Get<FTOptix.Modbus.Driver>(LogicObject.GetVariable("ModbusDriver").Value);
        if (myDriver == null)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, "ModbusDriver NodePointer is empty or invalid, please check settings");
            return false;
        }

        // Check CSV file path
        var csvPathVariable = LogicObject.Children.Get<IUAVariable>("CsvPath");
        csvFilePath = new ResourceUri(csvPathVariable.Value).Uri;
        if (string.IsNullOrEmpty(csvFilePath))
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, "Invalid CSV file path, please check settings");
            return false;
        }

        // Check field separator
        var separatorVariable = LogicObject.GetVariable("FieldSeparator");
        if (separatorVariable == null)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, "Field Separator variable not found");
            return false;
        }
        string separator = separatorVariable.Value;
        if (separator.Length != 1)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, "Wrong Field Separator configuration. Please insert a valid character");
            return false;
        }
        if (char.TryParse(separator, out char result))
        {
            fieldSeparator = result;
        }
        else
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, "Field separator cannot be parsed, please check syntax");
        }

        // Check wrap condition
        wrapFields = LogicObject.GetVariable("WrapFields").Value;

        return true;
    }

    private ICollection<IUANode> GetTagsFromRootRecursively(string root)
    {
        var objectsInFolder = new List<IUANode>();
        foreach (var childNode in Project.Current.Get(root).Children)
        {
            // Check if forbidden characters were used
            if (!wrapFields)
            {
                if (childNode.BrowseName.Contains(fieldSeparator))
                {
                    Log.Warning(MethodBase.GetCurrentMethod().Name,
                                $"Tag name \"{childNode.BrowseName}\" cannot contain the separator character, please enable the WrapFields option. This tag will not be exported.");
                    continue;
                }
                if (Log.Node(childNode).Contains(fieldSeparator))
                {
                    Log.Warning(MethodBase.GetCurrentMethod().Name,
                                $"Tag path \"{Log.Node(childNode)}\" cannot contain the separator character, please enable the WrapFields option. This tag will not be exported.");
                    continue;
                }
            }
            // Check for object type
            switch (childNode)
            {
                case FTOptix.Modbus.Tag:
                    objectsInFolder.Add(childNode);
                    break;

                case Folder:
                case TagStructure:
                case UAObject:
                    objectsInFolder.AddRange(GetTagsFromRootRecursively($"{root}/{childNode.BrowseName}"));
                    break;

                default:
                    Log.Debug(MethodBase.GetCurrentMethod().Name, $"Skipping object {Log.Node(childNode)}");
                    break;
            }
        }
        return objectsInFolder;
    }

    private int GenerateCsv(ICollection<IUANode> nodes)
    {
        Log.Info(MethodBase.GetCurrentMethod().Name, $"Writing {nodes.Count} variable(s) to CSV file");
        int exportedTags = 0;

        try
        {
            using var csvWriter = new CsvFileWriter(csvFilePath) { FieldDelimiter = fieldSeparator, WrapFields = wrapFields };
            // Write header
            csvWriter.WriteLine(new string[] { "VariableName", "Path", "IsStructure", "Type", "ArrayElements", "ArrayUpdateMode", "MaximumLength", "MemoryArea", "RegisterNumber", "BitOffset", "NumCoil", "NumDiscreteInput" });

            // Write content
            foreach (var item in nodes.OfType<FTOptix.Modbus.Tag>())
            {
                var varName = item.BrowseName;
                var path = ClearPathFromProjectInfo(Log.Node(item)).Replace("/" + varName, "");
                string isStructure = "false";
                if (InformationModel.Get(Project.Current.Get(path).NodeId).GetType().Name == "TagStructure")
                    isStructure = "true";
                var type = InformationModel.Get(item.DataType).BrowseName;
                uint[] arrDim = item.ArrayDimensions;
                uint arrayElements = 0;
                if (arrDim.Length > 0)
                    arrayElements = arrDim[0];
                var arrayUpdateMode = item.ArrayUpdateMode;
                var maxLength = item.MaximumLength;
                var memoryArea = item.MemoryArea;
                var registerNumber = item.NumRegister;
                var BitOffset = item.BitOffset;
                var numCoil = item.NumCoil;
                var numDiscreteInput = item.NumDiscreteInput;
                csvWriter.WriteLine(new string[] { varName, path, isStructure, type, arrayElements.ToString(), arrayUpdateMode.ToString(), maxLength.ToString(), memoryArea.ToString(), registerNumber.ToString(), BitOffset.ToString(), numCoil.ToString(), numDiscreteInput.ToString() });
                exportedTags++;
            }
        }
        catch (Exception e)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, "Unable to export Tags, error: " + e);
        }

        return exportedTags;
    }

    private static string ClearPathFromProjectInfo(string path)
    {
        var projectName = Project.Current.BrowseName + "/";
        var occurrence = path.IndexOf(projectName);
        if (occurrence == -1)
            return path;

        return path[(occurrence + projectName.Length)..];
    }

    private static CsvTagObject GetDataFromCsvRow(List<string> line, List<string> header, ref bool wrapFieldsDetected)
    {
        var csvTagObject = new CsvTagObject();

        // Check if text wrapping is present in the header
        if (!wrapFieldsDetected && header.Exists(h => h.StartsWith('\"') && h.EndsWith('\"')))
        {
            Log.Info(MethodBase.GetCurrentMethod().Name, "Text wrapping detected, trimming double quotes from data");
            wrapFieldsDetected = true;
        }

        // If text wrapping has been detected, trim content in the data line
        if (wrapFieldsDetected)
        {
            // Remove leading and trailing double quotes from the header
            header = header.ConvertAll(h => h.Trim('"'));
            // Remove leading and trailing double quotes from the data line
            line = line.ConvertAll(l => l.Trim('"'));
        }

        // Store the data in the CsvTagObject
        for (var columnIndex = 0; columnIndex < header.Count; columnIndex++)
        {
            csvTagObject.Variables.Add(header[columnIndex], line[columnIndex]);
        }

        return csvTagObject;
    }

    private sealed class CsvTagObject
    {
        public Dictionary<string, string> Variables { get; set; } = new Dictionary<string, string>();
    }

    private class CsvFileWriter : IDisposable
    {
        public char FieldDelimiter { get; set; } = ',';

        public char QuoteChar { get; set; } = '"';

        public bool WrapFields { get; set; } = false;

        public CsvFileWriter(string filePath)
        {
            _streamWriter = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
        }

        public void WriteLine(string[] fields)
        {
            var stringBuilder = new StringBuilder();

            for (var i = 0; i < fields.Length; ++i)
            {
                if (WrapFields)
                    stringBuilder.AppendFormat("{0}{1}{0}", QuoteChar, EscapeField(fields[i]));
                else
                    stringBuilder.AppendFormat("{0}", fields[i]);

                if (i != fields.Length - 1)
                    stringBuilder.Append(FieldDelimiter);
            }

            _streamWriter.WriteLine(stringBuilder.ToString());
            _streamWriter.Flush();
        }

        private string EscapeField(string field)
        {
            var quoteCharString = QuoteChar.ToString();
            return field.Replace(quoteCharString, quoteCharString + quoteCharString);
        }

        private readonly StreamWriter _streamWriter;

        #region IDisposable Support

        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _streamWriter.Dispose();
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    private string GetValueFromDictionary(Dictionary<string, string> dictionary, string key)
    {
        if (dictionary.TryGetValue(key, out string value))
            return value;
        
        throw new KeyNotFoundException($"Cannot read value of property \"key\" from CSV file");
    }

    public class TagParameters
    {
        public string TagPath { get; }
        public string TagName { get; }
        public string Type { get; }
        public bool IsStructure { get; }
        public uint ArrayElements { get; }
        public string ArrayUpdateMode { get; }
        public uint BitOffset { get; }
        public ushort NumCoil { get; }
        public ushort NumDiscreteInput { get; }
        public int MaximumLength { get; }
        public string MemoryArea { get; }
        public ushort RegisterNumber { get; }
        public bool IsFirstStructureElement { get; }

        // Constructor
        public TagParameters(string tagPath, string tagName, string type, bool isStructure, uint arrayElements,
                             string arrayUpdateMode, uint bitOffset, ushort numCoil, ushort numDiscreteInput,
                             int maximumLength, string memoryArea, ushort registerNumber,
                             bool isFirstStructureElement)
        {
            TagPath = tagPath;
            TagName = tagName;
            Type = type;
            IsStructure = isStructure;
            ArrayElements = arrayElements;
            ArrayUpdateMode = arrayUpdateMode;
            BitOffset = bitOffset;
            NumCoil = numCoil;
            NumDiscreteInput = numDiscreteInput;
            MaximumLength = maximumLength;
            MemoryArea = memoryArea;
            RegisterNumber = registerNumber;
            IsFirstStructureElement = isFirstStructureElement;
        }
    }

    private FTOptix.Modbus.Driver myDriver;
    private string csvFilePath;
    private char fieldSeparator = ';';
    private bool wrapFields;
    private LongRunningTask importTagsTask;
    private LongRunningTask exportTagsTask;
    private string lastStructure = "";
}
