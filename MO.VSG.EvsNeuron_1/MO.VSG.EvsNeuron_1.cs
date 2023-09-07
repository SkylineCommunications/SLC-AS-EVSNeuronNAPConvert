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
        private const int SdiFlowsOffset = 528;
        private const int MinDiscreetValueForSdiFlows = 528;
        private const int MaxDiscreetValueForSdiFlows = 561;

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

        private IEngine engine;
        private IDms dms;
        private DomHelper flowHelper;
        private DomHelper vsGroupHelper;
        private DomHelper levelsHelper;

        private int numberOfGeneratedSdiFlows;

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

            foreach (var element in dms.GetElements().Where(e => e.Protocol.Name == ProtocolName && e.Protocol.Version == "Production"))
            {
                var inputVirtualSignalGroups = GenerateSdiVirtualSignalGroups(element);
                var outputVirtualSignalGroups = GenerateIpVirtualSignalGroups(element);

                GenerateResources(element, inputVirtualSignalGroups, outputVirtualSignalGroups);
            }
        }

        private List<VideoPathData> GenerateSdiVirtualSignalGroups(IDmsElement element)
        {
            var sdiFlowInstances = GenerateSdiFlowInstances(element);
            numberOfGeneratedSdiFlows = sdiFlowInstances.Count;

            return GenerateSdiVirtualSignalGroups(element, sdiFlowInstances);
        }

        private List<VideoPathData> GenerateIpVirtualSignalGroups(IDmsElement element)
        {
            var macSettingsTable = element.GetTable(MACSettingsTableId);
            var macSettingsTableRows = macSettingsTable.GetData().Values;
            var mainStreamSourceIp = macSettingsTableRows.First().ToString();
            var secondaryStreamSourceIp = macSettingsTableRows.Last().ToString();

            Dictionary<string, DomInstance> ipAudioPrimaryFlowInstances = new Dictionary<string, DomInstance>();
            Dictionary<string, DomInstance> ipAudioSecondaryFlowInstances = new Dictionary<string, DomInstance>();
            Dictionary<string, DomInstance> ipVideoPrimaryFlowInstances = new Dictionary<string, DomInstance>();
            Dictionary<string, DomInstance> ipVideoSecondaryFlowInstances = new Dictionary<string, DomInstance>();

            var ipVideoOutputStreamsTable = element.GetTable(IPVideoOutputStreamsTableId);
            foreach (var row in ipVideoOutputStreamsTable.GetData().Values)
            {
                var flowInstance = GenerateFlowForMainStreamIpVideoOutputStreamsTable(element, row, mainStreamSourceIp);
                flowHelper.DomInstances.Create(flowInstance.Value);

                ipVideoPrimaryFlowInstances.Add(flowInstance.Key, flowInstance.Value);

                flowInstance = GenerateFlowForSecondaryStreamIpVideoOutputStreamsTable(element, row, secondaryStreamSourceIp);
                flowHelper.DomInstances.Create(flowInstance.Value);

                ipVideoSecondaryFlowInstances.Add(flowInstance.Key, flowInstance.Value);
            }

            var ipAudioOutputStreamsTable = element.GetTable(IPAudioOutputStreamsTableId);
            foreach (var row in ipAudioOutputStreamsTable.GetData().Values)
            {
                var flowInstance = GenerateFlowForMainStreamIpAudioOutputStreamsTable(element, row, mainStreamSourceIp);
                flowHelper.DomInstances.Create(flowInstance.Value);

                ipAudioPrimaryFlowInstances.Add(flowInstance.Key, flowInstance.Value);

                flowInstance = GenerateFlowForSecondaryStreamIpAudioOutputStreamsTable(element, row, secondaryStreamSourceIp);
                flowHelper.DomInstances.Create(flowInstance.Value);

                ipAudioSecondaryFlowInstances.Add(flowInstance.Key, flowInstance.Value);
            }

            var generatedVirtualSignalGroups = new List<VideoPathData>();

            var videoPathsTable = element.GetTable(VideoPathsTableId);
            foreach (var row in videoPathsTable.GetData().Values)
            {
                var virtualSignalGroupInstance = GenerateVirtualSignalGroupForIpInput(element, row);

                var flowKey = row[7].ToString();

                var flowInstance = ipVideoPrimaryFlowInstances[flowKey];
                AssignFlowToVirtualSignalGroup(virtualSignalGroupInstance, flowInstance, Level.Video, true);

                flowInstance = ipAudioPrimaryFlowInstances[flowKey];
                AssignFlowToVirtualSignalGroup(virtualSignalGroupInstance, flowInstance, Level.Audio1, true);

                flowInstance = ipVideoSecondaryFlowInstances[flowKey];
                AssignFlowToVirtualSignalGroup(virtualSignalGroupInstance, flowInstance, Level.Video, false);

                flowInstance = ipAudioSecondaryFlowInstances[flowKey];
                AssignFlowToVirtualSignalGroup(virtualSignalGroupInstance, flowInstance, Level.Audio1, false);

                var videoPathData = new VideoPathData
                {
                    Index = row[0].ToString(),
                    IsSource = true,
                    GeneratedVirtualSignalGroup = virtualSignalGroupInstance,
                };

                generatedVirtualSignalGroups.Add(videoPathData);
                
                vsGroupHelper.DomInstances.Create(virtualSignalGroupInstance);
            }

            return generatedVirtualSignalGroups;
        }

        private Dictionary<string, DomInstance> GenerateSdiFlowInstances(IDmsElement element)
        {
            var sdiFlowInstances = GenerateSdiFlowInstancesFromSdiStaticIoTable(element);
            sdiFlowInstances = sdiFlowInstances
                .Concat(GenerateSdiFlowInstancesFromSdiBiDirectionalTable(element))
                .ToLookup(x => x.Key, x => x.Value)
                .ToDictionary(x => x.Key, g => g.First());

            return sdiFlowInstances;
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
                // TODO: correct DCF interface
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
                // TODO: correct DCF interface
                var flowInstance = GenerateFlowSDI(element, row);
                flowHelper.DomInstances.Create(flowInstance.Value);

                sdiFlowInstances.Add(flowInstance.Key, flowInstance.Value);
            }

            return sdiFlowInstances;
        }

        private List<VideoPathData> GenerateSdiVirtualSignalGroups(IDmsElement element, Dictionary<string, DomInstance> sdiFlowInstances)
        {
            var generatedVirtualSignalGroups = new List<VideoPathData>();

            var tableVideoPaths = element.GetTable(VideoPathsTableId);
            var rows = tableVideoPaths.GetData().Values;

            foreach (var row in rows)
            {
                var virtualSignalGroupInstance = GenerateVirtualSignalGroupForSdiInput(element, row);

                var flowsAssigned = false;

                var mainInput = Convert.ToInt32(row[3].ToString());
                if (mainInput > MinDiscreetValueForSdiFlows &&
                    mainInput < MaxDiscreetValueForSdiFlows &&
                    (mainInput - SdiFlowsOffset) <= numberOfGeneratedSdiFlows)
                {
                    var flowKey = mainInput - SdiFlowsOffset;
                    var flowInstance = sdiFlowInstances[flowKey.ToString()];
                    AssignFlowToVirtualSignalGroup(virtualSignalGroupInstance, flowInstance, Level.Video, true);

                    flowsAssigned = true;
                }

                var backupInput = Convert.ToInt32(row[4].ToString());
                if (backupInput > MinDiscreetValueForSdiFlows &&
                    backupInput < MaxDiscreetValueForSdiFlows &&
                    (backupInput - SdiFlowsOffset) <= numberOfGeneratedSdiFlows)
                {
                    var flowKey = backupInput - SdiFlowsOffset;
                    var flowInstance = sdiFlowInstances[flowKey.ToString()];
                    AssignFlowToVirtualSignalGroup(virtualSignalGroupInstance, flowInstance, Level.Video, false);

                    flowsAssigned = true;
                }

                if (flowsAssigned)
                {
                    virtualSignalGroupInstance = vsGroupHelper.DomInstances.Create(virtualSignalGroupInstance);

                    var videoPathData = new VideoPathData
                    {
                        Index = row[0].ToString(),
                        IsSource = false,
                        GeneratedVirtualSignalGroup = virtualSignalGroupInstance,
                    };

                    generatedVirtualSignalGroups.Add(videoPathData);
                }
            }

            return generatedVirtualSignalGroups;
        }

        private void GenerateResources(IDmsElement element, List<VideoPathData> inputVirtualSignalGroups, List<VideoPathData> outputVirtualSignalGroups)
        {
            var resourceManagerHelper = new ResourceManagerHelper();

            var resourcePool = new ResourcePool() { Name = "Processors" };
            var processorsResourcePool = resourceManagerHelper
                .GetResourcePools(resourcePool)
                .SingleOrDefault(x => x.Name == "Processors");

            if (processorsResourcePool is null)
            {
                resourcePool.ID = Guid.NewGuid();
                resourceManagerHelper.AddOrUpdateResourcePools(resourcePool);
            }

            List<Resource> resources = new List<Resource>();

            var videoPathsTable = element.GetTable(VideoPathsTableId);
            foreach (var rowData in videoPathsTable.GetData().Values)
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

        private DomInstance GenerateVirtualSignalGroupForSdiInput(IDmsElement element, object[] row)
        {
            var index = row[0].ToString();

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
                new List<Guid>());
            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.AreaInfo.Definition,
                VirtualSignalGroup.Sections.AreaInfo.AreaIds,
                String.Empty);

            virtualSignalGroupInstance.AddOrUpdateListFieldValue(
                VirtualSignalGroup.Sections.DomainInfo.Definition,
                VirtualSignalGroup.Sections.DomainInfo.Domains,
                new List<Guid>());
            virtualSignalGroupInstance.AddOrUpdateFieldValue(
                VirtualSignalGroup.Sections.DomainInfo.Definition,
                VirtualSignalGroup.Sections.DomainInfo.DomainIds,
                String.Empty);

            return virtualSignalGroupInstance;
        }

        private DomInstance GenerateVirtualSignalGroupForIpInput(IDmsElement element, object[] row)
        {
            string index = row[0].ToString();

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

        private void AssignFlowToVirtualSignalGroup(DomInstance virtualSignalGroup, DomInstance flow, Level levelNumber, bool isBlueFlow)
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
            var levelInstance = levelsHelper.DomInstances
                .Read(DomInstanceExposers.FieldValues.DomInstanceField(Levels.Sections.Level.LevelNumber)
                .Equal((long)levelNumber))
                .FirstOrDefault();
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

            if (isBlueFlow)
            {
                var fieldValue = new FieldValue(
                    VirtualSignalGroup.Sections.LinkedFlows.BlueFlowId.ID,
                    ValueWrapperFactory.Create(flow.ID.Id));
                section.AddOrReplaceFieldValue(fieldValue);
            }
            else
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

        private KeyValuePair<string, DomInstance> GenerateFlowForMainStreamIpVideoOutputStreamsTable(IDmsElement element, object[] rowData, string sourceIp)
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
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, sourceIp);

            return new KeyValuePair<string, DomInstance>($"{index}", flowInstance);
        }

        private KeyValuePair<string, DomInstance> GenerateFlowForSecondaryStreamIpVideoOutputStreamsTable(IDmsElement element, object[] rowData, string sourceIp)
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
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, sourceIp);

            return new KeyValuePair<string, DomInstance>($"{index}", flowInstance);
        }

        private KeyValuePair<string, DomInstance> GenerateFlowForMainStreamIpAudioOutputStreamsTable(IDmsElement element, object[] rowData, string sourceIp)
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
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, sourceIp);

            return new KeyValuePair<string, DomInstance>($"{index}", flowInstance);
        }

        private KeyValuePair<string, DomInstance> GenerateFlowForSecondaryStreamIpAudioOutputStreamsTable(IDmsElement element, object[] rowData, string sourceIp)
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
            flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, sourceIp);

            return new KeyValuePair<string, DomInstance>($"{index}", flowInstance);
        }
    }

    public class VideoPathData
    {
        public string Index { get; set; }

        public DomInstance GeneratedVirtualSignalGroup { get; set; }

        public bool IsSource { get; set; }
    }
}