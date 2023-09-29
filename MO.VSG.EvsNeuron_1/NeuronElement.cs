using Skyline.DataMiner.Core.DataMinerSystem.Common;
using System;
using System.Collections.Generic;
using System.Linq;

internal class NeuronElement
{
    private const int DcfInterfaceTableId = 65049;
    private const int IpAudioOutputStreamsTableId = 3400;
    private const int IpVideoOutputStreamsTableId = 3200;
    private const int MacSettingsTableId = 1000;
    private const int SdiBidirectionalIoTableDirectionPid = 3102;
    private const int SdiBidirectionalIoTableId = 3100;
    private const int SdiBidirectionalIoTableInputDirectionValue = 131;
    private const int SdiStaticIoTableId = 1700;
    private const int SdiStaticIoTableInputStatusOkValue = 131;
    private const int SdiStaticIoTableStatusPid = 1702;
    private const int VideoPathsTableId = 2300;

    private static readonly Dictionary<string, string> VideoPathSelectionMappings = new Dictionary<string, string>
    {
        { "675", "A1" },
        { "676", "A2" },
        { "677", "A3" },
        { "678", "A4" },
        { "679", "B1" },
        { "680", "B2" },
        { "681", "B3" },
        { "682", "B4" },
        { "683", "C1" },
        { "684", "C2" },
        { "685", "C3" },
        { "686", "C4" },
        { "687", "D1" },
        { "688", "D2" },
        { "689", "D3" },
        { "690", "D4" },
    };

    private readonly Dictionary<string, string> AudioIndexPathMappings = new Dictionary<string, string>
    {
        { "1", "A1" },
        { "2", "A2" },
        { "3", "A3" },
        { "4", "A4" },
        { "5", "B1" },
        { "6", "B2" },
        { "7", "B3" },
        { "8", "B4" },
        { "9", "C1" },
        { "10", "C2" },
        { "11", "C3" },
        { "12", "C4" },
        { "13", "D1" },
        { "14", "D2" },
        { "15", "D3" },
        { "16", "D4" },
    };

    internal NeuronElement(IDmsElement dmsElement)
    {
        this.DmsElement = dmsElement;

        // Tables that are called more than once
        this.VideoPathTableRows = GetVideoPathTableRows(dmsElement);
        this.DcfInterfacesTableRows = GetDcfInterfacesTableRows(dmsElement);
    }

    public List<DcfInterfacesTableRow> DcfInterfacesTableRows { get; }

    public IDmsElement DmsElement { get; }

    public List<VideoPathTableRow> VideoPathTableRows { get; }

    //todo remake this after
    internal static List<MacSettingsTableRow> GetMacSettingsTableRows(IDmsElement element)
    {
        var table = element.GetTable(MacSettingsTableId);
        var valueRows = table.GetData().Values;

        var toReturn = new List<MacSettingsTableRow>();
        foreach (var row in valueRows)
        {
            toReturn.Add(new MacSettingsTableRow
            {
                Key = Convert.ToString(row[0]),
                IpAddress = Convert.ToString(row[2]),
            });
        }

        return toReturn;
    }

    [Obsolete]
    internal static IEnumerable<object[]> GetSdiBidirectionalIoTable(IDmsElement element)
    {
        var table = element.GetTable(SdiBidirectionalIoTableId);
        return table.QueryData(new[]
        {
            new ColumnFilter
            {
                ComparisonOperator = ComparisonOperator.Equal,
                Pid = SdiBidirectionalIoTableDirectionPid,
                Value = Convert.ToString(SdiBidirectionalIoTableInputDirectionValue),
            },
        });
    }

    [Obsolete]
    internal static IEnumerable<object[]> GetSdiStaticIoTable(IDmsElement element)
    {
        var table = element.GetTable(SdiStaticIoTableId);
        return table.QueryData(new[]
        {
            new ColumnFilter
            {
                ComparisonOperator = ComparisonOperator.Equal,
                Pid = SdiStaticIoTableStatusPid,
                Value = Convert.ToString(SdiStaticIoTableInputStatusOkValue),
            },
        });
    }

    internal static List<VideoPathTableRow> GetVideoPathTableRows(IDmsElement element)
    {
        var table = element.GetTable(VideoPathsTableId);
        var valueRows = table.GetData().Values;

        var toReturn = new List<VideoPathTableRow>();
        foreach (var row in valueRows)
        {
            toReturn.Add(new VideoPathTableRow
            {
                Key = Convert.ToString(row[0]),
                MainInput = Convert.ToInt32(row[3]),
                BackupInput = Convert.ToInt32(row[4]),
            });
        }

        return toReturn;
    }

    internal string GetDcfInterfaceId(int parameterGroupId, string key)
    {
        var interfaceDynamicLink = String.Join(";", parameterGroupId, key);
        var dcfInterfaceRow = this.DcfInterfacesTableRows.Find(i => i.InterfaceDynamicLink == interfaceDynamicLink);
        return dcfInterfaceRow?.Key;
    }

    internal List<IpOutputStreamTableRow> GetIpAudioOutputStreamTableRows()
    {
        var table = this.DmsElement.GetTable(IpAudioOutputStreamsTableId);
        var valueRows = table.GetData().Values;

        var toReturn = new List<IpOutputStreamTableRow>();
        foreach (var row in valueRows)
        {
            var key = Convert.ToString(row[0]);
            toReturn.Add(new IpOutputStreamTableRow
            {
                Key = key,
                PrimaryDestinationPort = Convert.ToInt32(row[3]),
                PrimaryDestinationIp = Convert.ToString(row[4]),
                SecondaryDestinationPort = Convert.ToInt32(row[11]),
                SecondaryDestinationIp = Convert.ToString(row[12]),
                Path = AudioIndexPathMappings[key],
            });
        }

        return toReturn;
    }

    internal List<IpOutputStreamTableRow> GetIpVideoOutputStreamTableRows()
    {
        var table = this.DmsElement.GetTable(IpVideoOutputStreamsTableId);
        var valueRows = table.GetData().Values;

        var toReturn = new List<IpOutputStreamTableRow>();
        foreach (var row in valueRows)
        {
            toReturn.Add(new IpOutputStreamTableRow
            {
                Key = Convert.ToString(row[0]),
                PrimaryDestinationPort = Convert.ToInt32(row[5]),
                PrimaryDestinationIp = Convert.ToString(row[6]),
                Path = VideoPathSelectionMappings[Convert.ToString(row[9])],
                SecondaryDestinationPort = Convert.ToInt32(row[13]),
                SecondaryDestinationIp = Convert.ToString(row[14]),
            });
        }

        return toReturn;
    }

    internal List<string> GetSdiBidirectionalTableKeys()
    {
        return DmsElement.GetTable(SdiBidirectionalIoTableId).GetPrimaryKeys().ToList();
    }

    internal List<string> GetSdiStaticIoTableKeys()
    {
        return DmsElement.GetTable(SdiStaticIoTableId).GetPrimaryKeys().ToList();
    }

    private static List<DcfInterfacesTableRow> GetDcfInterfacesTableRows(IDmsElement element)
    {
        var table = element.GetTable(DcfInterfaceTableId);
        var valueRows = table.GetData().Values;
        var toReturn = new List<DcfInterfacesTableRow>();
        foreach (var row in valueRows)
        {
            toReturn.Add(new DcfInterfacesTableRow
            {
                Key = Convert.ToString(row[0]),
                InterfaceDynamicLink = Convert.ToString(row[5]),
            });
        }

        return toReturn;
    }

    internal class BaseTableRow
    {
        protected BaseTableRow()
        { }

        internal string Key { get; set; }
    }

    internal class DcfInterfacesTableRow : BaseTableRow
    {
        internal string InterfaceDynamicLink { get; set; }
    }

    internal class IpOutputStreamTableRow : BaseTableRow
    {
        internal string Path { get; set; }

        internal string PrimaryDestinationIp { get; set; }

        internal long PrimaryDestinationPort { get; set; }

        internal string SecondaryDestinationIp { get; set; }

        internal long SecondaryDestinationPort { get; set; }
    }

    internal class MacSettingsTableRow : BaseTableRow
    {
        internal string IpAddress { get; set; }
    }

    internal class VideoPathTableRow : BaseTableRow
    {
        internal int BackupInput { get; set; }

        internal int MainInput { get; set; }
    } 
}