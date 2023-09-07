/*
****************************************************************************
*  Copyright (c) 2023,  Skyline Communications NV  All Rights Reserved.    *
****************************************************************************

By using this script, you expressly agree with the usage terms and
conditions set out below.
This script and all related materials are protected by copyrights and
other intellectual property rights that exclusively belong
to Skyline Communications.

A user license granted for this script is strictly for personal use only.
This script may not be used in any way by anyone without the prior
written consent of Skyline Communications. Any sublicensing of this
script is forbidden.

Any modifications to this script by the user are only allowed for
personal use and within the intended purpose of the script,
and will remain the sole responsibility of the user.
Skyline Communications will not be responsible for any damages or
malfunctions whatsoever of the script resulting from a modification
or adaptation by the user.

The content of this script is confidential information.
The user hereby agrees to keep this confidential information strictly
secret and confidential and not to disclose or reveal it, in whole
or in part, directly or indirectly to any person, entity, organization
or administration without the prior written consent of
Skyline Communications.

Any inquiries can be addressed to:

	Skyline Communications NV
	Ambachtenstraat 33
	B-8870 Izegem
	Belgium
	Tel.	: +32 51 31 35 69
	Fax.	: +32 51 31 01 29
	E-mail	: info@skyline.be
	Web		: www.skyline.be
	Contact	: Ben Vandenberghe

****************************************************************************
Revision History:

DATE		VERSION		AUTHOR			COMMENTS

05/09/2023	1.0.0.1		RRA, Skyline	Initial version
****************************************************************************
*/

namespace MO.VSG.EvsNeuron_1
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;

    using Skyline.DataMiner.Automation;
    using Skyline.DataMiner.Core.DataMinerSystem.Automation;
    using Skyline.DataMiner.Core.DataMinerSystem.Common;
    using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;
    using Skyline.DataMiner.Net.Apps.Sections.Sections;
    using Skyline.DataMiner.Net.Messages;
    using Skyline.DataMiner.Net.Messages.SLDataGateway;
    using Skyline.DataMiner.Net.Sections;
    using Skyline.DataMiner.Utils.MediaOps.DomDefinitions;
    using Skyline.DataMiner.Utils.MediaOps.DomDefinitions.Enums;

    /// <summary>
    /// Represents a DataMiner Automation script.
    /// </summary>
    public class Script
    {
        private const string ProtocolName = "EVS Neuron NAP - CONVERT";

        private const int MACSettingsTableId = 1000;
        private const int IpAddressMACSettingsPID = 1003;

        private const int SdiStaticIOTableId = 1700;
        private const int InputStatusStaticIOPID = 1702;
        private const int InputStatusOkValue = 131;

        private const int SdiBidirectionalIOTableId = 3100;
        private const int DirectionSdiBidirectionalIOPID = 3102;
        private const int InputDirectionValue = 131;

        private const int VideoPathsTableId = 2300;
        private const int MainInputVideoPathsPID = 2304;
        private const int BackupInputVideoPathsPID = 2305;

        private const int IPVideoOutputStreamsTableId = 3200;
        private const int IPAudioOutputStreamsTableId = 3400;
        private const int SDIFlowsOffset = 528;
        private const int MinDiscreetValueForSdiFlows = 528;
        private const int MaxDiscreetValueForSdiFlows = 561;
        private const int NumberOfGeneratedSdiFlows = 28;

        private readonly Dictionary<string, string> pathSelectionDiscreetMap = new Dictionary<string, string>()
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

        private IDms dms;
        private IEngine engine;
        private DomHelper flowHelper;
        private DomHelper vsGroupHelper;
        private DomHelper levelsHelper;

        /// <summary>
        /// The script entry point.
        /// </summary>
        /// <param name="engine">Link with SLAutomation process.</param>
        public void Run(IEngine engine)
        {
            try
            {
                this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
                dms = engine.GetDms();

                flowHelper = new DomHelper(engine.SendSLNetMessages, Flows.ModuleSettings.ModuleId);
                vsGroupHelper = new DomHelper(engine.SendSLNetMessages, VirtualSignalGroup.ModuleSettings.ModuleId);
                levelsHelper = new DomHelper(engine.SendSLNetMessages, Levels.ModuleSettings.ModuleId);

                List<IDmsElement> elements = GetEvsNeuronNAPConvertElements(engine);
                List<VideoPathData> genratedVSGs = new List<VideoPathData>();

                foreach (IDmsElement element in elements)
                {
                    var sdiFlowInstances = GenerateSdiFlowInstancesFromSdiStaticIoTable(element);
                    sdiFlowInstances = sdiFlowInstances
                        .Concat(GenerateSdiFlowInstancesFromSdiBiDirectionalTable(element))
                        .ToLookup(x => x.Key, x => x.Value)
                        .ToDictionary(x => x.Key, g => g.First());


                    Dictionary<string, DomInstance> ipAudioPrimaryFlowInstances = new Dictionary<string, DomInstance>();
                    Dictionary<string, DomInstance> ipAudioSecondaryFlowInstances = new Dictionary<string, DomInstance>();
                    Dictionary<string, DomInstance> ipVideoPrimaryFlowInstances = new Dictionary<string, DomInstance>();
                    Dictionary<string, DomInstance> ipVideoSecondaryFlowInstances = new Dictionary<string, DomInstance>();

                    // Generating VSGs from Video Paths Table
                    IEnumerable<object[]> dataVideoPaths = GetDataFromVideoPathsTable(element);
                    engine.GenerateInformation("VideoPaths Count: " + dataVideoPaths.Count());
                    foreach (var rowData in dataVideoPaths)
                    {
                        var mainInput = Convert.ToInt32(rowData[3].ToString());
                        var backupInput = Convert.ToInt32(rowData[4].ToString());

                        var vsgInstance = GenerateVSGForSDIInput(element, rowData);
                        engine.GenerateInformation("VSGInstanceId: " + vsgInstance.ID.Id);

                        var vsgContainsFlows = false;

                        if (mainInput > MinDiscreetValueForSdiFlows && mainInput < MaxDiscreetValueForSdiFlows && (mainInput - SDIFlowsOffset) <= NumberOfGeneratedSdiFlows)
                        {
                            int flowKey = mainInput - SDIFlowsOffset;
                            var flowInstance = sdiFlowInstances[flowKey.ToString()];
                            vsgContainsFlows = true;
                            AssignFlowToVirtualSignalGroup(vsgInstance, flowInstance, Level.Video, "mainInput");
                        }

                        if (backupInput > MinDiscreetValueForSdiFlows && backupInput < MaxDiscreetValueForSdiFlows && (backupInput - SDIFlowsOffset) <= NumberOfGeneratedSdiFlows)
                        {
                            var flowKey = backupInput - SDIFlowsOffset;
                            var flowInstance = sdiFlowInstances[flowKey.ToString()];
                            vsgContainsFlows = true;
                            AssignFlowToVirtualSignalGroup(vsgInstance, flowInstance, Level.Video, "backupInput");
                        }

                        if (vsgContainsFlows)
                        {
                            genratedVSGs.Add(new VideoPathData() { Index = rowData[0].ToString(), IsSource = false, GeneratedVsg = vsgInstance });
                            vsGroupHelper.DomInstances.Create(vsgInstance);
                        }
                    }
                    engine.GenerateInformation("Done!");
                    IEnumerable<object[]> dataMACSettings = GetDataFromMACSettingsTable(element);
                    IEnumerable<object[]> dataIPVideoOutputStreams = GetDataFromIPVideoOutputStreamsTable(element);

                    var mainStreamSourceIp = dataMACSettings.First().ToString(); // todo, need to make better logic for this
                    var secondaryStreamSourceIP = dataMACSettings.Last().ToString(); // todo, need to make better logic for this
                    engine.GenerateInformation("Done2!");
                    foreach (var rowData in dataIPVideoOutputStreams)
                    {
                        var flowInstance = GenerateFlowForMainStreamIpVideoOutputStreamsTable(element, rowData, mainStreamSourceIp);
                        ipVideoPrimaryFlowInstances.Add(flowInstance.Key, flowInstance.Value);
                        engine.GenerateInformation("2flowInstance.Key: " + flowInstance.Key);
                        flowHelper.DomInstances.Create(flowInstance.Value);

                        flowInstance = GenerateFlowForSecondaryStreamIpVideoOutputStreamsTable(element, rowData, secondaryStreamSourceIP);
                        ipVideoSecondaryFlowInstances.Add(flowInstance.Key, flowInstance.Value);
                        engine.GenerateInformation("2flowInstance.Key: " + flowInstance.Key);
                        flowHelper.DomInstances.Create(flowInstance.Value);
                    }

                    IEnumerable<object[]> dataIPAudioOutputStreams = GetDataFromIPAudioOutputStreamsTable(element);

                    foreach (var rowData in dataIPAudioOutputStreams)
                    {
                        var flowInstance = GenerateFlowForMainStreamIpAudioOutputStreamsTable(element, rowData, mainStreamSourceIp);
                        ipAudioPrimaryFlowInstances.Add(flowInstance.Key, flowInstance.Value);
                        flowHelper.DomInstances.Create(flowInstance.Value);

                        flowInstance = GenerateFlowForSecondaryStreamIpAudioOutputStreamsTable(element, rowData, secondaryStreamSourceIP);
                        ipAudioSecondaryFlowInstances.Add(flowInstance.Key, flowInstance.Value);
                        flowHelper.DomInstances.Create(flowInstance.Value);
                    }

                    dataVideoPaths = GetDataFromVideoPathsTableWithoutFiltering(element);

                    foreach (var rowData in dataVideoPaths)
                    {
                        string flowKey = rowData[7].ToString();
                        //var mainInput = rowData[3].ToString();
                        //var backupInput = rowData[4].ToString();

                        var vsgInstance = GenerateVSGForIpOutput(element, rowData);

                        var flowInstance = ipVideoPrimaryFlowInstances[flowKey];
                        AssignFlowToVirtualSignalGroup(vsgInstance, flowInstance, Level.Video, "mainInput");

                        flowInstance = ipAudioPrimaryFlowInstances[flowKey];
                        AssignFlowToVirtualSignalGroup(vsgInstance, flowInstance, Level.Audio1, "mainInput");

                        flowInstance = ipVideoSecondaryFlowInstances[flowKey];
                        AssignFlowToVirtualSignalGroup(vsgInstance, flowInstance, Level.Video, "backupInput");

                        flowInstance = ipAudioSecondaryFlowInstances[flowKey];
                        AssignFlowToVirtualSignalGroup(vsgInstance, flowInstance, Level.Audio1, "backupInput");

                        genratedVSGs.Add(new VideoPathData() { Index = rowData[0].ToString(), IsSource = true, GeneratedVsg = vsgInstance });
                        vsGroupHelper.DomInstances.Create(vsgInstance);
                    }

                    //GenerateResources(element);
                }
            }
            catch (Exception ex)
            {
                engine.GenerateInformation(ex.Message);
                engine.Log(ex.ToString());
            }
        }

        private Dictionary<string, DomInstance> GenerateSdiFlowInstancesFromSdiStaticIoTable(IDmsElement element)
        {
            var sdiFlowInstances = new Dictionary<string, DomInstance>();

            var table = element.GetTable(SdiStaticIOTableId);
            var rows = table.QueryData(new[]
            {
                    new ColumnFilter
                    {
                        ComparisonOperator = ComparisonOperator.Equal,
                        Pid = InputStatusStaticIOPID,
                        Value = Convert.ToString(InputStatusOkValue),
                    },
            });

            foreach (var row in rows)
            {
                var flowInstance = GenerateFlowSDI(element, row);
                flowHelper.DomInstances.Create(flowInstance.Value);

                sdiFlowInstances.Add(flowInstance.Key, flowInstance.Value);
            }

            return sdiFlowInstances;
        }

        private Dictionary<string, DomInstance> GenerateSdiFlowInstancesFromSdiBiDirectionalTable(IDmsElement element)
        {
            var sdiFlowInstances = new Dictionary<string, DomInstance>();

            var table = element.GetTable(SdiBidirectionalIOTableId);
            var rows = table.QueryData(new[]
            {
                    new ColumnFilter
                    {
                        ComparisonOperator = ComparisonOperator.Equal,
                        Pid = DirectionSdiBidirectionalIOPID,
                        Value = Convert.ToString(InputDirectionValue),
                    },
            });

            foreach (var row in rows)
            {
                var flowInstance = GenerateFlowSDI(element, row);
                flowHelper.DomInstances.Create(flowInstance.Value);

                sdiFlowInstances.Add(flowInstance.Key, flowInstance.Value);
            }

            return sdiFlowInstances;
        }

        private void GenerateResources(IDmsElement element)
        {
            var resourceManagerHelper = new ResourceManagerHelper();
            var videoPathTableData = GetDataFromVideoPathsTableWithoutFiltering(element);
            ResourcePool resourcePool = new ResourcePool() { Name = "Processors" };
            var processorsResourcePool = resourceManagerHelper.GetResourcePools(resourcePool).SingleOrDefault(x => x.Name == "Processors"); // todo, maybe filter query can be removed

            if (processorsResourcePool is null)
            {
                resourcePool.ID = Guid.NewGuid();
                resourceManagerHelper.AddOrUpdateResourcePools(resourcePool);
            }

            List<Resource> resources = new List<Resource>();
            foreach (var rowData in videoPathTableData)
            {
                Guid resourceId = Guid.NewGuid();
                var resource = new Resource()
                {
                    MaxConcurrency = 1000,
                    Properties = new List<ResourceManagerProperty>()
                    {
                        new ResourceManagerProperty()
                        {
                            Name = "Path",
                            Value = $"{element.Name} Index",
                        },
                        new ResourceManagerProperty()
                        {
                            Name = "input VSGs",
                            Value = "A1",
                        },
                        new ResourceManagerProperty()
                        {
                            Name = "output VSGs",
                            Value = "A1",
                        },
                    },

                    PoolGUIDs = new List<Guid> { resourcePool.ID },
                    Mode = ResourceMode.Available,
                    ID = resourceId,
                    GUID = resourceId,
                };

                // add capabilit....
                resources.Add(resource);
            }

            //resourceManagerHelper.AddOrUpdateResources(resources.ToArray());
        }

        private IEnumerable<object[]> GetDataFromMACSettingsTable(IDmsElement element)
        {
            var tableMACSettings = element.GetTable(MACSettingsTableId);
            return tableMACSettings.GetData().Values;
        }

        private IEnumerable<object[]> GetDataFromIPAudioOutputStreamsTable(IDmsElement element)
        {
            var tableIPAudioOutputStreams = element.GetTable(IPAudioOutputStreamsTableId);
            return tableIPAudioOutputStreams.GetRows();
        }

        private IEnumerable<object[]> GetDataFromIPVideoOutputStreamsTable(IDmsElement element)
        {
            var tableIPVideoOutputStreams = element.GetTable(IPVideoOutputStreamsTableId);
            return tableIPVideoOutputStreams.GetRows();
        }

        private IEnumerable<object[]> GetDataFromVideoPathsTableWithoutFiltering(IDmsElement element)
        {
            var tableVideoPaths = element.GetTable(VideoPathsTableId);
            return tableVideoPaths.GetRows();
        }

        private IEnumerable<object[]> GetDataFromVideoPathsTable(IDmsElement element)
        {
            var tableVideoPaths = element.GetTable(VideoPathsTableId);
            return tableVideoPaths.QueryData(new[]
            {
                 new ColumnFilter()
                 {
                     ComparisonOperator = ComparisonOperator.GreaterThanOrEqual,
                     Pid = MainInputVideoPathsPID,
                     Value = Convert.ToString(529),
                 },
                 new ColumnFilter()
                 {
                     ComparisonOperator = ComparisonOperator.LessThanOrEqual,
                     Pid = MainInputVideoPathsPID,
                     Value = Convert.ToString(560),
                 },
            });
        }

        private IEnumerable<object[]> GetDataFromSDIBidirectionalTable(IDmsElement element)
        {
            var tableSdiBidirectionalIO = element.GetTable(SdiBidirectionalIOTableId);
            return tableSdiBidirectionalIO.QueryData(new[]
            {
                    new ColumnFilter()
                    {
                        ComparisonOperator= ComparisonOperator.Equal,
                        Pid = DirectionSdiBidirectionalIOPID,
                        Value = Convert.ToString(InputDirectionValue),
                    },
            });
        }

        private List<IDmsElement> GetEvsNeuronNAPConvertElements(IEngine engine)
        {
            IDmsProtocol protocol = dms.GetProtocol(ProtocolName, "Production");
            var elements = dms.GetElements().Where(e => e.Protocol.Name == protocol.Name).ToList();

            return elements;
        }

        private DomInstance GenerateVSGForSDIInput(IDmsElement element, object[] rowData)
        {
            string index = rowData[0].ToString();

            var virtualSignalGroupInstance = new DomInstance
            {
                DomDefinitionId = VirtualSignalGroup.DomDefinition.ID,
            };

            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.Info.Definition,
                VirtualSignalGroup.Sections.Info.Name,
                $"{element.Name} {index}");
            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.Info.Definition,
                VirtualSignalGroup.Sections.Info.Role,
                (int)Role.Destination);
            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.Info.Definition,
                VirtualSignalGroup.Sections.Info.OperationalState,
                (int)OperationalState.Up);
            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.Info.Definition,
                VirtualSignalGroup.Sections.Info.AdministrativeState,
                (int)AdministrativeState.Up);

            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.Info.Definition,
                VirtualSignalGroup.Sections.Info.Type,
                Guid.Empty);

            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.SystemLabels.Definition,
                VirtualSignalGroup.Sections.SystemLabels.ButtonLabel,
                $"{element.Name} {index}");

            virtualSignalGroupInstance.AddOrUpdateListFieldValue(
                VirtualSignalGroup.Sections.AreaInfo.Definition,
                VirtualSignalGroup.Sections.AreaInfo.Areas,
                new List<Guid>
                {
                });
            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.AreaInfo.Definition,
                VirtualSignalGroup.Sections.AreaInfo.AreaIds,
                String.Empty);

            virtualSignalGroupInstance.AddOrUpdateListFieldValue(
                VirtualSignalGroup.Sections.DomainInfo.Definition,
                VirtualSignalGroup.Sections.DomainInfo.Domains,
                new List<Guid>
                {
                });
            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.DomainInfo.Definition,
                VirtualSignalGroup.Sections.DomainInfo.DomainIds,
                String.Empty);

            return virtualSignalGroupInstance;
        }

        private DomInstance GenerateVSGForIpOutput(IDmsElement element, object[] rowData)
        {
            string index = rowData[0].ToString();

            var virtualSignalGroupInstance = new DomInstance
            {
                DomDefinitionId = VirtualSignalGroup.DomDefinition.ID,
            };

            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.Info.Definition,
                VirtualSignalGroup.Sections.Info.Name,
                element.Name + index);
            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.Info.Definition,
                VirtualSignalGroup.Sections.Info.Role,
                (int)Role.Source);
            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.Info.Definition,
                VirtualSignalGroup.Sections.Info.OperationalState,
                (int)OperationalState.Up);
            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.Info.Definition,
                VirtualSignalGroup.Sections.Info.AdministrativeState,
                (int)AdministrativeState.Up);

            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.Info.Definition,
                VirtualSignalGroup.Sections.Info.Type,
                Guid.Empty);

            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.SystemLabels.Definition,
                VirtualSignalGroup.Sections.SystemLabels.ButtonLabel,
                element.Name + index);

            virtualSignalGroupInstance.AddOrUpdateListFieldValue(
                VirtualSignalGroup.Sections.AreaInfo.Definition,
                VirtualSignalGroup.Sections.AreaInfo.Areas,
                new List<Guid>
                {
                });
            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.AreaInfo.Definition,
                VirtualSignalGroup.Sections.AreaInfo.AreaIds,
                String.Empty);

            virtualSignalGroupInstance.AddOrUpdateListFieldValue(
                VirtualSignalGroup.Sections.DomainInfo.Definition,
                VirtualSignalGroup.Sections.DomainInfo.Domains,
                new List<Guid>
                {
                });
            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.DomainInfo.Definition,
                VirtualSignalGroup.Sections.DomainInfo.DomainIds,
                String.Empty);

            return virtualSignalGroupInstance;
        }

        private void AssignFlowToVirtualSignalGroup(DomInstance virtualSignalGroup, DomInstance flow, Level levelNumber, string inputType)
        {
            flow.AddOrUpdateListFieldValue(
                Flows.Sections.FlowGroup.Definition,
                Flows.Sections.FlowGroup.LinkedSignalGroup,
                new List<Guid> { virtualSignalGroup.ID.Id });
            flow.AddOrUpdateFieldValue(
                Flows.Sections.FlowGroup.Definition,
                Flows.Sections.FlowGroup.LinkedSignalGroupIds,
                virtualSignalGroup.ID.Id.ToString());

            // todo: do not get each time, Maybe we can get that once at the beggining of the script. Check with Joey that.
            var levelInstance = levelsHelper.DomInstances.Read(DomInstanceExposers.FieldValues.DomInstanceField(Levels.Sections.Level.LevelNumber).Equal((long)levelNumber)).FirstOrDefault();
            if (levelInstance == null)
            {
                return;
            }

            var section = virtualSignalGroup.Sections
                .Find(s => s.FieldValues
                    .Any(f => f.FieldDescriptorID.Equals(VirtualSignalGroup.Sections.LinkedFlows.FlowLevel.ID) &&
                        f.Value.Equals(ValueWrapperFactory.Create(levelInstance.ID.Id))));
            if (section == null)
            {
                section = new Section(VirtualSignalGroup.Sections.LinkedFlows.Definition.ID);

                var levelFieldValue = new FieldValue(
                    VirtualSignalGroup.Sections.LinkedFlows.FlowLevel.ID,
                    ValueWrapperFactory.Create(levelInstance.ID.Id));
                section.AddOrReplaceFieldValue(levelFieldValue);
            }

            if (inputType == "mainInput")
            {
                var fieldValue = new FieldValue(
                    VirtualSignalGroup.Sections.LinkedFlows.BlueFlowId.ID,
                    ValueWrapperFactory.Create(flow.ID.Id));
                section.AddOrReplaceFieldValue(fieldValue);
            }

            if (inputType == "backupInput")
            {
                var fieldValue = new FieldValue(
                    VirtualSignalGroup.Sections.LinkedFlows.RedFlowId.ID,
                    ValueWrapperFactory.Create(flow.ID.Id));
                section.AddOrReplaceFieldValue(fieldValue);
            }

            virtualSignalGroup.Sections.Add(section);
        }

        private KeyValuePair<string, DomInstance> GenerateFlowSDI(IDmsElement element, object[] rowData)
        {
            var flowInstance = new DomInstance
            {
                DomDefinitionId = Flows.DomDefinition.ID,
            };

            string channelId = rowData[0].ToString();
            engine.GenerateInformation($"Flow intance name: {element.Name} SDI {channelId}");
            flowInstance.ID = new DomInstanceId(Guid.NewGuid());
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name, $"{element.Name} SDI {channelId}");
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.FlowDirection, (int)FlowDirection.Rx);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.OperationalState, (int)OperationalState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.AdministrativeState, (int)AdministrativeState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.TransportType, (int)TransportType.Sdi);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Element, element.DmsElementId.Value);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.SubInterface, String.Empty);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Interface, channelId);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.PathOrder, 0L);

            return new KeyValuePair<string, DomInstance>($"{channelId}", flowInstance);
        }

        private KeyValuePair<string, DomInstance> GenerateFlowForMainStreamIpVideoOutputStreamsTable(IDmsElement element, object[] rowData, string sourceIP)
        {
            string index = rowData[7].ToString();
            long destinationPort = Convert.ToInt64(rowData[5].ToString());
            string destinationIp = rowData[6].ToString();

            string pathSelection = pathSelectionDiscreetMap[index];

            var flowInstance = new DomInstance
            {
                DomDefinitionId = Flows.DomDefinition.ID,
            };
            flowInstance.ID = new DomInstanceId(Guid.NewGuid());
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name, $"{element.Name} Main Stream {pathSelection}");
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.FlowDirection, (int)FlowDirection.Tx);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.OperationalState, (int)OperationalState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.AdministrativeState, (int)AdministrativeState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.TransportType, (int)TransportType.St2110_20);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Element, element.DmsElementId.Value);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.SubInterface, String.Empty);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Interface, pathSelection);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.PathOrder, 0L);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationPort, destinationPort);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationIp, destinationIp);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, sourceIP);

            return new KeyValuePair<string, DomInstance>($"{index}", flowInstance);
        }

        private KeyValuePair<string, DomInstance> GenerateFlowForSecondaryStreamIpVideoOutputStreamsTable(IDmsElement element, object[] rowData, string sourceIP)
        {
            string index = rowData[7].ToString();
            long secondaryDestinationPort = Convert.ToInt64(rowData[13].ToString());
            string secondaryDestinationIp = rowData[14].ToString();

            string pathSelection = pathSelectionDiscreetMap[index];

            var flowInstance = new DomInstance
            {
                DomDefinitionId = Flows.DomDefinition.ID,
            };
            flowInstance.ID = new DomInstanceId(Guid.NewGuid());
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name, $"{element.Name} Secondary Stream {pathSelection}");
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.FlowDirection, (int)FlowDirection.Tx);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.OperationalState, (int)OperationalState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.AdministrativeState, (int)AdministrativeState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.TransportType, (int)TransportType.St2110_20);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Element, element.DmsElementId.Value);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.SubInterface, String.Empty);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Interface, pathSelection);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.PathOrder, 0L);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationPort, secondaryDestinationPort);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationIp, secondaryDestinationIp);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, sourceIP);

            return new KeyValuePair<string, DomInstance>($"{index}", flowInstance);
        }

        private KeyValuePair<string, DomInstance> GenerateFlowForMainStreamIpAudioOutputStreamsTable(IDmsElement element, object[] rowData, string sourceIP)
        {
            string index = rowData[7].ToString();
            long destinationPort = Convert.ToInt64(rowData[5].ToString());
            string destinationIp = rowData[6].ToString();

            string pathSelection = pathSelectionDiscreetMap[index];

            var flowInstance = new DomInstance
            {
                DomDefinitionId = Flows.DomDefinition.ID,
            };
            flowInstance.ID = new DomInstanceId(Guid.NewGuid());
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name, $"{element.Name} Main Stream {pathSelection}");
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.FlowDirection, (int)FlowDirection.Tx);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.OperationalState, (int)OperationalState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.AdministrativeState, (int)AdministrativeState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.TransportType, (int)TransportType.St2110_30);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Element, element.DmsElementId.Value);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.SubInterface, String.Empty);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Interface, pathSelection);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.PathOrder, 0L);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationPort, destinationPort);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationIp, destinationIp);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, sourceIP);

            return new KeyValuePair<string, DomInstance>($"{index}", flowInstance);
        }

        private KeyValuePair<string, DomInstance> GenerateFlowForSecondaryStreamIpAudioOutputStreamsTable(IDmsElement element, object[] rowData, string sourceIP)
        {
            string index = rowData[7].ToString();
            long secondaryDestinationPort = Convert.ToInt64(rowData[13].ToString());
            string secondaryDestinationIp = rowData[14].ToString();

            string pathSelection = pathSelectionDiscreetMap[index];

            var flowInstance = new DomInstance
            {
                DomDefinitionId = Flows.DomDefinition.ID,
            };
            flowInstance.ID = new DomInstanceId(Guid.NewGuid());
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name, $"{element.Name} Secondary Stream {pathSelection}");
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.FlowDirection, (int)FlowDirection.Tx);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.OperationalState, (int)OperationalState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.AdministrativeState, (int)AdministrativeState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.TransportType, (int)TransportType.St2110_30);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Element, element.DmsElementId.Value);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.SubInterface, String.Empty);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Interface, pathSelection);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.PathOrder, 0L);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationPort, secondaryDestinationPort);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationIp, secondaryDestinationIp);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, sourceIP);

            return new KeyValuePair<string, DomInstance>($"{index}", flowInstance);
        }

        //private int CalculateIndexForIpFlow(string indexValue)
        //{
        //    int index = Int32.Parse(Regex.Match(indexValue, @"\d+").Value);

        //    if (indexValue.Contains("B"))
        //    {
        //        index += 4;
        //    }
        //    else if (indexValue.Contains("C"))
        //    {
        //        index += 8;
        //    }
        //    else if (indexValue.Contains("D"))
        //    {
        //        index += 12;
        //    }

        //    return index;
        //}
    }

    public class VideoPathData
    {
        public string Index { get; set; }

        public DomInstance GeneratedVsg { get; set; }

        public bool IsSource { get; set; }
    }
}