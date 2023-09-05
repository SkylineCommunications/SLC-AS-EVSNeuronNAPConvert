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

namespace MO.VSG.EvsNeuron_Generate_static_flows_and_resources_for_processor_1
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
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            dms = engine.GetDms();
            flowHelper = new DomHelper(engine.SendSLNetMessages, Flows.ModuleSettings.ModuleId);
            vsGroupHelper = new DomHelper(engine.SendSLNetMessages, VirtualSignalGroup.ModuleSettings.ModuleId);
            levelsHelper = new DomHelper(engine.SendSLNetMessages, Levels.ModuleSettings.ModuleId);

            List<IDmsElement> elements = GetEvsNeuronNAPConvertElements(engine);
            List<VideoPathData> genratedVSGs = new List<VideoPathData>();

            if (elements.Any())
            {
                foreach (IDmsElement element in elements)
                {
                    Dictionary<string, DomInstance> sdiFlowInstances = new Dictionary<string, DomInstance>();
                    Dictionary<string, DomInstance> ipAudioFlowInstances = new Dictionary<string, DomInstance>();
                    Dictionary<string, DomInstance> ipVideoFlowInstances = new Dictionary<string, DomInstance>();

                    IEnumerable<object[]> dataSdiStaticIO = GetDataFromSDIStaticIOTable(element);

                    foreach (var rowData in dataSdiStaticIO)
                    {
                        var flowInstance = GenerateFlowSDI(element, rowData);
                        sdiFlowInstances.Add(flowInstance.Key, flowInstance.Value);
                    }

                    IEnumerable<object[]> dataSdiBidirectionalIO = GetDataFromSDIBidirectionalTable(element);

                    foreach (var rowData in dataSdiBidirectionalIO)
                    {
                        var flowInstance = GenerateFlowSDI(element, rowData);
                        sdiFlowInstances.Add(flowInstance.Key, flowInstance.Value);
                    }

                    IEnumerable<object[]> dataVideoPaths = GetDataFromVideoPathsTable(element);

                    foreach (var rowData in dataVideoPaths)
                    {
                        var mainInput = Convert.ToInt32(rowData[3].ToString());
                        var backupInput = Convert.ToInt32(rowData[4].ToString());
                        engine.GenerateInformation("E");
                        var vsgInstance = GenerateVSGForSDIInput(element, rowData);

                        //int flowKey = Int32.Parse(Regex.Match(mainInput, @"\d+").Value);
                        int flowKey = mainInput - SDIFlowsOffset;

                        var flowInstance = sdiFlowInstances[flowKey.ToString()];
                        AssignFlowToVirtualSignalGroup(vsgInstance, flowInstance, Level.Video, "mainInput");

                        //flowHelper.DomInstances.Create(flowInstance);

                        if (backupInput > 528 && backupInput < 561)
                        {
                            //flowKey = Int32.Parse(Regex.Match(backupInput, @"\d+").Value);
                            flowKey = backupInput - SDIFlowsOffset;

                            flowInstance = sdiFlowInstances[flowKey.ToString()];
                            AssignFlowToVirtualSignalGroup(vsgInstance, flowInstance, Level.Video, "backupInput");

                            //flowHelper.DomInstances.Create(flowInstance);
                        }

                        genratedVSGs.Add(new VideoPathData() { Index = rowData[0].ToString(), IsSource = false, GeneratedVsg = vsgInstance });
                        //vsGroupHelper.DomInstances.Create(vsgInstance);
                    }

                    IEnumerable<object[]> dataMACSettings = GetDataFromMACSettingsTable(element);

                    IEnumerable<object[]> dataIPVideoOutputStreams = GetDataFromIPVideoOutputStreamsTable(element);

                    var mainStreamSourceIp = dataMACSettings.First()[2].ToString(); // todo, need to make better logic for this
                    var secondaryStreamSourceIP = dataMACSettings.Last()[2].ToString(); // todo, need to make better logic for this
                    foreach (var rowData in dataIPVideoOutputStreams)
                    {
                        var flowInstance = GenerateFlowForMainStreamIpVideoOutputStreamsTable(element, rowData, mainStreamSourceIp);
                        ipVideoFlowInstances.Add(flowInstance.Key, flowInstance.Value);
                        flowInstance = GenerateFlowForSecondaryStreamIpVideoOutputStreamsTable(element, rowData, secondaryStreamSourceIP);
                        ipVideoFlowInstances.Add(flowInstance.Key, flowInstance.Value);
                    }

                    IEnumerable<object[]> dataIPAudioOutputStreams = GetDataFromIPAudioOutputStreamsTable(element);

                    foreach (var rowData in dataIPAudioOutputStreams)
                    {
                        var flowInstance = GenerateFlowForMainStreamIpAudioOutputStreamsTable(element, rowData, mainStreamSourceIp);
                        ipAudioFlowInstances.Add(flowInstance.Key, flowInstance.Value);
                        flowInstance = GenerateFlowForSecondaryStreamIpAudioOutputStreamsTable(element, rowData, secondaryStreamSourceIP);
                        ipAudioFlowInstances.Add(flowInstance.Key, flowInstance.Value);
                    }

                    dataVideoPaths = GetDataFromVideoPathsTableWithoutFiltering(element);

                    foreach (var rowData in dataVideoPaths)
                    {
                        var mainInput = rowData[3].ToString();
                        var backupInput = rowData[4].ToString();

                        var vsgInstance = GenerateVSGForIpOutput(element, rowData);

                        int flowKey = Int32.Parse(Regex.Match(mainInput, @"\d+").Value);

                        var flowInstance = ipVideoFlowInstances[flowKey.ToString()];
                        AssignFlowToVirtualSignalGroup(vsgInstance, flowInstance, Level.Video, "mainInput");

                        //flowHelper.DomInstances.Create(flowInstance);

                        flowInstance = ipAudioFlowInstances[flowKey.ToString()];
                        AssignFlowToVirtualSignalGroup(vsgInstance, flowInstance, Level.Audio1, "mainInput");

                        //flowHelper.DomInstances.Create(flowInstance);

                        flowKey = Int32.Parse(Regex.Match(backupInput, @"\d+").Value);

                        flowInstance = ipVideoFlowInstances[flowKey.ToString()];
                        AssignFlowToVirtualSignalGroup(vsgInstance, flowInstance, Level.Video, "backupInput");

                        //flowHelper.DomInstances.Create(flowInstance);

                        flowInstance = ipVideoFlowInstances[flowKey.ToString()];
                        AssignFlowToVirtualSignalGroup(vsgInstance, flowInstance, Level.Audio1, "backupInput");

                        //flowHelper.DomInstances.Create(flowInstance);

                        genratedVSGs.Add(new VideoPathData() { Index = rowData[0].ToString(), IsSource = true, GeneratedVsg = vsgInstance });
                        //vsGroupHelper.DomInstances.Create(vsgInstance);
                    }

                    GenerateResources(element);

                    return;
                }
            }
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
            var tableMACSettings = element.GetTable(1000);
            return tableMACSettings.GetData(1003).Values;
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

        private IEnumerable<object[]> GetDataFromSDIStaticIOTable(IDmsElement element)
        {
            var tableSdiStaticIO = element.GetTable(SdiStaticIOTableId);
            return tableSdiStaticIO.QueryData(new[]
            {
                    new ColumnFilter()
                    {
                        ComparisonOperator= ComparisonOperator.Equal,
                        Pid = InputStatusStaticIOPID,
                        Value = Convert.ToString(InputStatusOkValue),
                    },
            });
        }

        private List<IDmsElement> GetEvsNeuronNAPConvertElements(IEngine engine)
        {
            IDmsProtocol protocol = dms.GetProtocol(ProtocolName, "Production");
            var elements = dms.GetElements().Where(e => e.Protocol.Name == protocol.Name).ToList();

            engine.GenerateInformation("List of elements: " + String.Join(";", elements));
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
                element.Name + index);
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
            var levelInstance = levelsHelper.DomInstances.Read(DomInstanceExposers.FieldValues.DomInstanceField(Levels.Sections.Level.LevelNumber).Equal(levelNumber)).FirstOrDefault();
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
            string channelId = rowData[0].ToString();

            DomInstance flowInstance = new DomInstance();
            flowInstance.ID = new DomInstanceId(Guid.NewGuid());
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name, $"{element.Name} SDI {channelId}");
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.FlowDirection, FlowDirection.Rx);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowInfo.OperationalState, OperationalState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowInfo.AdministrativeState, AdministrativeState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.TransportType, TransportType.Sdi);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Element, element.DmsElementId.Value);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowPath.SubInterface, String.Empty);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowPath.Interface, channelId);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.PathOrder, 0L);

            return new KeyValuePair<string, DomInstance>($"{channelId}", flowInstance);
        }

        private KeyValuePair<string, DomInstance> GenerateFlowForMainStreamIpVideoOutputStreamsTable(IDmsElement element, object[] rowData, string sourceIP)
        {
            string index = rowData[0].ToString();
            long destinationPort = Convert.ToInt64(rowData[5].ToString());
            string destinationIp = rowData[6].ToString();

            DomInstance flowInstance = new DomInstance();
            flowInstance.ID = new DomInstanceId(Guid.NewGuid());
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name, $"{element.Name} Main Stream {index}");
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.FlowDirection, FlowDirection.Tx);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowInfo.OperationalState, OperationalState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowInfo.AdministrativeState, AdministrativeState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.TransportType, TransportType.St2110_20);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Element, element.DmsElementId.Value);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowPath.SubInterface, String.Empty);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowPath.Interface, index);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.PathOrder, 0L);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationPort, destinationPort);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationIp, destinationIp);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, sourceIP);

            return new KeyValuePair<string, DomInstance>($"{index}", flowInstance);
        }

        private KeyValuePair<string, DomInstance> GenerateFlowForSecondaryStreamIpVideoOutputStreamsTable(IDmsElement element, object[] rowData, string sourceIP)
        {
            string index = rowData[0].ToString();
            long secondaryDestinationPort = Convert.ToInt64(rowData[13].ToString());
            string secondaryDestinationIp = rowData[14].ToString();

            DomInstance flowInstance = new DomInstance();
            flowInstance.ID = new DomInstanceId(Guid.NewGuid());
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name, $"{element.Name} Secondary Stream {index}");
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.FlowDirection, FlowDirection.Tx);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowInfo.OperationalState, OperationalState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowInfo.AdministrativeState, AdministrativeState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.TransportType, TransportType.St2110_20);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Element, element.DmsElementId.Value);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowPath.SubInterface, String.Empty);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowPath.Interface, index);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.PathOrder, 0L);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationPort, secondaryDestinationPort);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationIp, secondaryDestinationIp);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, sourceIP);

            return new KeyValuePair<string, DomInstance>($"{index}", flowInstance);
        }

        private KeyValuePair<string, DomInstance> GenerateFlowForMainStreamIpAudioOutputStreamsTable(IDmsElement element, object[] rowData, string sourceIP)
        {
            string index = rowData[0].ToString();
            long destinationPort = Convert.ToInt64(rowData[5].ToString());
            string destinationIp = rowData[6].ToString();

            DomInstance flowInstance = new DomInstance();
            flowInstance.ID = new DomInstanceId(Guid.NewGuid());
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name, $"{element.Name} Main Stream {index}");
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.FlowDirection, FlowDirection.Tx);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowInfo.OperationalState, OperationalState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowInfo.AdministrativeState, AdministrativeState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.TransportType, TransportType.St2110_30);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Element, element.DmsElementId.Value);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowPath.SubInterface, String.Empty);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowPath.Interface, index);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.PathOrder, 0L);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationPort, destinationPort);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationIp, destinationIp);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, sourceIP);

            return new KeyValuePair<string, DomInstance>($"{index}", flowInstance);
        }

        private KeyValuePair<string, DomInstance> GenerateFlowForSecondaryStreamIpAudioOutputStreamsTable(IDmsElement element, object[] rowData, string sourceIP)
        {
            string index = rowData[0].ToString();
            long secondaryDestinationPort = Convert.ToInt64(rowData[13].ToString());
            string secondaryDestinationIp = rowData[14].ToString();

            DomInstance flowInstance = new DomInstance();
            flowInstance.ID = new DomInstanceId(Guid.NewGuid());
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name, $"{element.Name} Secondary Stream {index}");
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.FlowDirection, FlowDirection.Tx);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowInfo.OperationalState, OperationalState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowInfo.AdministrativeState, AdministrativeState.Up);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.TransportType, TransportType.St2110_30);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Element, element.DmsElementId.Value);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowPath.SubInterface, String.Empty);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowPath.Interface, index);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.PathOrder, 0L);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationPort, secondaryDestinationPort);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationIp, secondaryDestinationIp);
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, sourceIP);

            return new KeyValuePair<string, DomInstance>($"{index}", flowInstance);
        }
    }

    public class VideoPathData
    {
        public string Index { get; set; }

        public DomInstance GeneratedVsg { get; set; }

        public bool IsSource { get; set; }
    }
}