using System;
using System.Collections.Generic;
using Skyline.DataMiner.Core.DataMinerSystem.Common;

internal class NeuronElement
{
    private const int DcfInterfaceTableId = 65049;
    private const int MacSettingsTableId = 1000;
    private const int SdiBidirectionalIoTableDirectionPid = 3102;
    private const int SdiBidirectionalIoTableId = 3100;
    private const int SdiBidirectionalIoTableInputDirectionValue = 131;
    private const int SdiStaticIoTableId = 1700;
    private const int SdiStaticIoTableInputStatusOkValue = 131;
    private const int SdiStaticIoTableStatusPid = 1702;
    private const int VideoPathsTableId = 2300;

    internal NeuronElement(IDmsElement dmsElement)
    {
        // Tables that are called more than once
        this.VideoPathsTable = dmsElement.GetTable(VideoPathsTableId);
        this.DcfInterfacesTableRows = GetDcfInterfacesTableRows(dmsElement);
    }

    public List<DcfInterfacesTableRow> DcfInterfacesTableRows { get; }

    public IDmsTable DcfInterfaceTable { get; }

    public IDmsTable VideoPathsTable { get; }

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

    //todo remake this after
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