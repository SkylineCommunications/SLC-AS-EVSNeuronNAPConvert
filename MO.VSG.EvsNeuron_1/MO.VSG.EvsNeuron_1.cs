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

using System;
using System.Collections.Generic;
using System.Linq;
using NeuronElementLib;
using Skyline.DataMiner.Automation;
using Skyline.DataMiner.Core.DataMinerSystem.Automation;
using Skyline.DataMiner.Core.DataMinerSystem.Common;
using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;
using Skyline.DataMiner.Net.Apps.Sections.Sections;
using Skyline.DataMiner.Net.ManagerStore;
using Skyline.DataMiner.Net.Messages;
using Skyline.DataMiner.Net.Messages.SLDataGateway;
using Skyline.DataMiner.Net.Profiles;
using Skyline.DataMiner.Net.Sections;
using Skyline.DataMiner.Utils.MediaOps.DomDefinitions;
using Skyline.DataMiner.Utils.MediaOps.DomDefinitions.Enums;

/// <summary>
/// Represents a DataMiner Automation script.
/// </summary>
public class Script
{
    private const int MacSettingsTableDcfParameterGroupId = 5;
    private const int MaxDiscreetValueForSdiFlows = 561;
    private const int MinDiscreetValueForSdiFlows = 528;
    private const string ProtocolName = "EVS Neuron NAP - CONVERT";
    private const int SdiBidirectionalIoTableDcfParameterGroupId = 2;
    private const int SdiFlowsOffset = 528;
    private const int SdiStaticIoTableDcfParameterGroupId = 1;

    private IEnumerable<DomInstance> CurrentFlows { get; set; }

    private List<Resource> CurrentResources { get; set; }

    private IEnumerable<DomInstance> CurrentVSGroups { get; set; }

    private DomHelper FlowHelper { get; set; }

    private DomHelper LevelsHelper { get; set; }

    private Skyline.DataMiner.Net.Profiles.Parameter LinkedSourceParameter { get; set; }

    private ResourcePool ResourcePool { get; set; }

    private DomHelper VSGroupHelper { get; set; }

    /// <summary>
    /// The script entry point.
    /// </summary>
    /// <param name="engine">Link with SLAutomation process.</param>
    public void Run(IEngine engine)
    {
        var dms = engine.GetDms();

        VSGroupHelper = new DomHelper(engine.SendSLNetMessages, VirtualSignalGroup.ModuleSettings.ModuleId);
        FlowHelper = new DomHelper(engine.SendSLNetMessages, Flows.ModuleSettings.ModuleId);
        LevelsHelper = new DomHelper(engine.SendSLNetMessages, Levels.ModuleSettings.ModuleId);

        var resourceManagerHelper = new ResourceManagerHelper(engine.SendSLNetSingleResponseMessage);
        ResourcePool = GetResourcePool(resourceManagerHelper, "Processors");
        this.LinkedSourceParameter = GetLinkedSourceParameter(engine);

        this.CurrentVSGroups = VSGroupHelper.DomInstances.ReadAll();
        this.CurrentFlows = FlowHelper.DomInstances.ReadAll();
        this.CurrentResources = resourceManagerHelper.GetResources(new TRUEFilterElement<Resource>()).ToList();

        // todo  check if duplicates as errors can occur if any - 
        //CheckIfDuplicates();

        var allElements = dms.GetElements().Where(e => e.Protocol.Name == ProtocolName && e.Protocol.Version == "Production");
        CheckAndRemoveResources(resourceManagerHelper, allElements);

        var activeElements = allElements.Where(e => e.State == Skyline.DataMiner.Core.DataMinerSystem.Common.ElementState.Active);

        var resourcesToAddOrUpdate = new List<Resource>();
        foreach (var element in activeElements)
        {
            // Get NeuronElementLib element with the tables
            var neuronElement = new NeuronElement(element);

            // SDIs
            var sdiFlows = GetSdiFlows(neuronElement);

            // IPs
            var macSettingsTableRows = NeuronElementLib.NeuronElement.GetMacSettingsTableRows(element);
            var ipVideoFlows = GetIpVideoFlows(neuronElement, macSettingsTableRows);
            var ipAudioFlows = GetIpAudioFlows(neuronElement, macSettingsTableRows);

            foreach (var videoPathTableRow in neuronElement.VideoPathTableRows)
            {
                // Create SDI VSGroups
                var sdiVSGroup = GetSdiVSGroup(element, sdiFlows, videoPathTableRow);

                // Create IP VSGroup
                var ipVSGroup = GetIpVSGroup(element, ipVideoFlows, ipAudioFlows, videoPathTableRow.Key);

                var resource = GetResource(element, videoPathTableRow.Key, sdiVSGroup, ipVSGroup);
                resourcesToAddOrUpdate.Add(resource);
            }
        }

        if (resourcesToAddOrUpdate.Any())
            resourceManagerHelper.AddOrUpdateResources(resourcesToAddOrUpdate.ToArray());
    }

    private static void CheckAndRemoveResources(ResourceManagerHelper resourceManagerHelper, IEnumerable<IDmsElement> elements)
    {
        // Remove resources to which there is no element present anymore
        var elementIds = elements.Select(e => e.DmsElementId.Value).Distinct().ToList();
        var resources = resourceManagerHelper.GetResources(new TRUEFilterElement<Resource>());
        var resourcesToRemove = resources.Where(resource => !elementIds.Contains($"{resource.DmaID}/{resource.ElementID}")).ToList();
        if (resourcesToRemove.Any())
        {
            resourceManagerHelper.RemoveResources(resourcesToRemove.ToArray());
        }
    }

    private static void CreateOrUpdateDomInstance(DomHelper helper, IEnumerable<DomInstance> currentInstances, DomInstance newInstance, string instanceName)
    {
        var currentInstance = currentInstances.FirstOrDefault(i => i.Name == instanceName);
        if (currentInstance != null)
        {
            // Keep the previous ID and update
            newInstance.ID = currentInstance.ID;
            helper.DomInstances.Update(newInstance);
        }
        else
        {
            // create new one
            newInstance.ID = new DomInstanceId(Guid.NewGuid());
            helper.DomInstances.Create(newInstance);
        }
    }

    private static DomInstance GenerateVSGroup(string elementName, string key, Role role)
    {
        var inputOutput = role == Role.Destination ? "Input" : "Output";

        var instance = new DomInstance
        {
            DomDefinitionId = VirtualSignalGroup.DomDefinition.ID,
        };

        instance.AddOrUpdateFieldValue(
            VirtualSignalGroup.Sections.Info.Definition,
            VirtualSignalGroup.Sections.Info.Name,
            $"{elementName} {key} {inputOutput}");
        instance.AddOrUpdateFieldValue(
            VirtualSignalGroup.Sections.Info.Definition,
            VirtualSignalGroup.Sections.Info.Role,
            (int)Role.Destination);
        instance.AddOrUpdateFieldValue(
            VirtualSignalGroup.Sections.Info.Definition,
            VirtualSignalGroup.Sections.Info.OperationalState,
            (int)OperationalState.Up);
        instance.AddOrUpdateFieldValue(
            VirtualSignalGroup.Sections.Info.Definition,
            VirtualSignalGroup.Sections.Info.AdministrativeState,
            (int)AdministrativeState.Up);

        instance.AddOrUpdateFieldValue(
            VirtualSignalGroup.Sections.Info.Definition,
            VirtualSignalGroup.Sections.Info.Type,
            Guid.Empty);

        instance.AddOrUpdateFieldValue(
            VirtualSignalGroup.Sections.SystemLabels.Definition,
            VirtualSignalGroup.Sections.SystemLabels.ButtonLabel,
            $"{elementName} {key}");

        instance.AddOrUpdateListFieldValue(
            VirtualSignalGroup.Sections.AreaInfo.Definition,
            VirtualSignalGroup.Sections.AreaInfo.Areas,
            new List<Guid>());
        instance.AddOrUpdateFieldValue(
            VirtualSignalGroup.Sections.AreaInfo.Definition,
            VirtualSignalGroup.Sections.AreaInfo.AreaIds,
            string.Empty);

        instance.AddOrUpdateListFieldValue(
            VirtualSignalGroup.Sections.DomainInfo.Definition,
            VirtualSignalGroup.Sections.DomainInfo.Domains,
            new List<Guid>());
        instance.AddOrUpdateFieldValue(
            VirtualSignalGroup.Sections.DomainInfo.Definition,
            VirtualSignalGroup.Sections.DomainInfo.DomainIds,
            string.Empty);

        return instance;
    }

    private static DomInstance GetFlowInstance(TransportType transportType, DmsElementId dmsElementId, string dcfInterfaceId)
    {
        var instance = new DomInstance
        {
            DomDefinitionId = Flows.DomDefinition.ID,
        };

        FlowDirection flowDirection = transportType == TransportType.Sdi ? FlowDirection.Rx : FlowDirection.Tx;
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.FlowDirection, (int)flowDirection);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.OperationalState, (int)OperationalState.Up);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.AdministrativeState, (int)AdministrativeState.Up);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.TransportType, (int)transportType);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Element, dmsElementId.Value);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.Interface, dcfInterfaceId ?? string.Empty);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.PathOrder, 0L);

        return instance;
    }

    private static DomInstance GetIpAudioPrimaryFlowInstance(NeuronElement neuron, IpOutputStreamTableRow ipAudioTableRow, MacSettingsTableRow macSettingsTableRow)
    {
        IDmsElement dmsElement = neuron.DmsElement;
        var dcfInterfaceId = neuron.GetDcfInterfaceId(MacSettingsTableDcfParameterGroupId, macSettingsTableRow.Key);

        var instance = GetFlowInstance(TransportType.St2110_30, dmsElement.DmsElementId, dcfInterfaceId);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name, $"{dmsElement.Name} Main Audio Stream {ipAudioTableRow.Key}");
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.SubInterface, ipAudioTableRow.Key);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationPort, ipAudioTableRow.PrimaryDestinationPort);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationIp, ipAudioTableRow.PrimaryDestinationIp);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, macSettingsTableRow.IpAddress);

        return instance;
    }

    private static DomInstance GetIpAudioSecondaryFlowInstance(NeuronElement neuron, IpOutputStreamTableRow ipAudioTableRow, MacSettingsTableRow macSettingsTableRow)
    {
        IDmsElement dmsElement = neuron.DmsElement;
        var dcfInterfaceId = neuron.GetDcfInterfaceId(MacSettingsTableDcfParameterGroupId, macSettingsTableRow.Key);

        var instance = GetFlowInstance(TransportType.St2110_30, dmsElement.DmsElementId, dcfInterfaceId);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name, $"{dmsElement.Name} Secondary Audio Stream {ipAudioTableRow.Key}");
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.SubInterface, ipAudioTableRow.Key);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationPort, ipAudioTableRow.SecondaryDestinationPort);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationIp, ipAudioTableRow.SecondaryDestinationIp);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, macSettingsTableRow.IpAddress);

        return instance;
    }

    private static DomInstance GetIpVideoPrimaryFlowInstance(NeuronElement neuron, IpOutputStreamTableRow ipVideoTableRow, MacSettingsTableRow macSettingsTableRow)
    {
        IDmsElement dmsElement = neuron.DmsElement;
        var dcfInterfaceId = neuron.GetDcfInterfaceId(MacSettingsTableDcfParameterGroupId, macSettingsTableRow.Key);

        var instance = GetFlowInstance(TransportType.St2110_20, dmsElement.DmsElementId, dcfInterfaceId);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name, $"{dmsElement.Name} Main Video Stream {ipVideoTableRow.Path}");
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.SubInterface, ipVideoTableRow.Key);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationPort, ipVideoTableRow.PrimaryDestinationPort);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationIp, ipVideoTableRow.PrimaryDestinationIp);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, macSettingsTableRow.IpAddress);

        return instance;
    }

    private static DomInstance GetIpVideoSecondaryFlowInstance(NeuronElement neuron, IpOutputStreamTableRow ipVideoTableRow, MacSettingsTableRow macSettingsTableRow)
    {
        IDmsElement dmsElement = neuron.DmsElement;
        var dcfInterfaceId = neuron.GetDcfInterfaceId(MacSettingsTableDcfParameterGroupId, macSettingsTableRow.Key);

        var instance = GetFlowInstance(TransportType.St2110_20, dmsElement.DmsElementId, dcfInterfaceId);

        instance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name, $"{dmsElement.Name} Secondary Video Stream {ipVideoTableRow.Path}");
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.SubInterface, ipVideoTableRow.Key);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationPort, ipVideoTableRow.SecondaryDestinationPort);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.DestinationIp, ipVideoTableRow.SecondaryDestinationIp);
        instance.AddOrUpdateFieldValue(Flows.Sections.FlowTransport.Definition, Flows.Sections.FlowTransport.SourceIp, macSettingsTableRow.IpAddress);

        return instance;
    }

    private static Skyline.DataMiner.Net.Profiles.Parameter GetLinkedSourceParameter(IEngine engine)
    {
        var profileManagerHelper = new ProfileHelper(engine.SendSLNetMessages);
        var profileParameter = profileManagerHelper.ProfileParameters.Read(ExposerExtensions.Equal(ParameterExposers.Name, "Linked Source")).FirstOrDefault();
        if (profileParameter == null)
        {
            profileParameter = new Skyline.DataMiner.Net.Profiles.Parameter
            {
                ID = Guid.NewGuid(),
                Name = "Linked Source",
                Categories = ProfileParameterCategory.Capability,
                Type = Skyline.DataMiner.Net.Profiles.Parameter.ParameterType.Text,
            };

            profileManagerHelper.ProfileParameters.Create(profileParameter);
        }

        return profileParameter;
    }

    private static ResourcePool GetResourcePool(ResourceManagerHelper resourceManagerHelper, string name, bool createIfNotFound = true)
    {
        var resourcePool = resourceManagerHelper.GetResourcePools(new ResourcePool { Name = name }).FirstOrDefault();
        if (resourcePool == null && createIfNotFound)
        {
            resourcePool = new ResourcePool
            {
                ID = Guid.NewGuid(),
                Name = "Processors",
            };

            resourceManagerHelper.AddOrUpdateResourcePools(resourcePool);
        }

        return resourcePool;
    }

    private static DomInstance GetSdiFlowInstance(IDmsElement element, string key, string dcfInterface)
    {
        var flowInstance = GetFlowInstance(TransportType.Sdi, element.DmsElementId, dcfInterface);
        flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name, $"{element.Name} SDI {key}");
        flowInstance.AddOrUpdateFieldValue(Flows.Sections.FlowPath.Definition, Flows.Sections.FlowPath.SubInterface, string.Empty);

        return flowInstance;
    }

    private void AssignFlowToVirtualSignalGroup(DomInstance vsgroup, DomInstance flow, Level levelNumber, FlowType flowType)
    {
        flow.AddOrUpdateListFieldValue(
            Flows.Sections.FlowGroup.Definition,
            Flows.Sections.FlowGroup.LinkedSignalGroup,
            new List<Guid> { vsgroup.ID.Id });
        flow.AddOrUpdateFieldValue(
            Flows.Sections.FlowGroup.Definition,
            Flows.Sections.FlowGroup.LinkedSignalGroupIds,
            vsgroup.ID.Id.ToString());

        var levelInstance = LevelsHelper.DomInstances
            .Read(DomInstanceExposers.FieldValues.DomInstanceField(Levels.Sections.Level.LevelNumber)
            .Equal((long)levelNumber))
            .FirstOrDefault();
        if (levelInstance == null)
        {
            return;
        }

        var section = vsgroup.Sections
            .Find(s => s.FieldValues
                .Any(f => f.FieldDescriptorID.Equals(VirtualSignalGroup.Sections.LinkedFlows.FlowLevel.ID) &&
                    f.Value.Equals(ValueWrapperFactory.Create(levelInstance.ID.Id))));
        if (section == null)
        {
            section = new Section(VirtualSignalGroup.Sections.LinkedFlows.Definition.ID);
            var levelFieldValue = new FieldValue(VirtualSignalGroup.Sections.LinkedFlows.FlowLevel.ID, ValueWrapperFactory.Create(levelInstance.ID.Id));
            section.AddOrReplaceFieldValue(levelFieldValue);
            vsgroup.Sections.Add(section);
        }

        if (flowType == FlowType.Blue)
        {
            var fieldValue = new FieldValue(VirtualSignalGroup.Sections.LinkedFlows.BlueFlowId.ID, ValueWrapperFactory.Create(flow.ID.Id));
            section.AddOrReplaceFieldValue(fieldValue);
        }
        else /*if(flowType == FlowType.Red)*/
        {
            var fieldValue = new FieldValue(VirtualSignalGroup.Sections.LinkedFlows.RedFlowId.ID, ValueWrapperFactory.Create(flow.ID.Id));
            section.AddOrReplaceFieldValue(fieldValue);
        }
    }

    private void CreateOrUpdateFlow(DomInstance newInstance)
    {
        var name = newInstance.GetFieldValue<string>(Flows.Sections.FlowInfo.Definition, Flows.Sections.FlowInfo.Name).Value;
        CreateOrUpdateDomInstance(this.FlowHelper, this.CurrentFlows, newInstance, name);
    }

    private void CreateOrUpdateVSGroup(DomInstance newInstance)
    {
        var name = newInstance.GetFieldValue<string>(VirtualSignalGroup.Sections.Info.Definition, VirtualSignalGroup.Sections.Info.Name).Value;
        CreateOrUpdateDomInstance(this.VSGroupHelper, this.CurrentVSGroups, newInstance, name);
    }

    private Dictionary<string, IpFlow> GetIpAudioFlows(NeuronElement neuron, List<MacSettingsTableRow> macSettingsTableRows)
    {
        var flows = new Dictionary<string, IpFlow>();
        var ipAudioTableRows = neuron.GetIpAudioOutputStreamTableRows();

        var primaryMacSettings = macSettingsTableRows[0];
        var secondaryMacSettings = macSettingsTableRows[1];

        foreach (var ipAudioRow in ipAudioTableRows)
        {
            var primaryFlow = GetIpAudioPrimaryFlowInstance(neuron, ipAudioRow, primaryMacSettings);
            CreateOrUpdateFlow(primaryFlow);

            var secondaryFlow = GetIpAudioSecondaryFlowInstance(neuron, ipAudioRow, secondaryMacSettings);
            CreateOrUpdateFlow(secondaryFlow);

            flows.Add(ipAudioRow.Path, new IpFlow
            {
                Primary = primaryFlow,
                Secondary = secondaryFlow,
            });
        }

        return flows;
    }

    private Dictionary<string, IpFlow> GetIpVideoFlows(NeuronElement neuron, List<MacSettingsTableRow> macSettingsTableRows)
    {
        var flows = new Dictionary<string, IpFlow>();
        var ipVideoTableRows = neuron.GetIpVideoOutputStreamTableRows();

        var primaryMacSettings = macSettingsTableRows[0];
        var secondaryMacSettings = macSettingsTableRows[1];

        foreach (var ipVideoRow in ipVideoTableRows)
        {
            var primaryFlow = GetIpVideoPrimaryFlowInstance(neuron, ipVideoRow, primaryMacSettings);
            CreateOrUpdateFlow(primaryFlow);

            var secondaryFlow = GetIpVideoSecondaryFlowInstance(neuron, ipVideoRow, secondaryMacSettings);
            CreateOrUpdateFlow(secondaryFlow);

            flows.Add(ipVideoRow.Path, new IpFlow
            {
                Primary = primaryFlow,
                Secondary = secondaryFlow,
            });
        }

        return flows;
    }

    private DomInstance GetIpVSGroup(IDmsElement element, Dictionary<string, IpFlow> ipVideoFlows, Dictionary<string, IpFlow> ipAudioFlows, string videoPathKey)
    {
        string key = videoPathKey;
        var vsgroup = GenerateVSGroup(element.Name, key, Role.Source);

        var ipVideos = ipVideoFlows[key];
        AssignFlowToVirtualSignalGroup(vsgroup, ipVideos.Primary, Level.Video, FlowType.Blue);
        AssignFlowToVirtualSignalGroup(vsgroup, ipVideos.Secondary, Level.Video, FlowType.Red);

        var ipAudios = ipAudioFlows[key];
        AssignFlowToVirtualSignalGroup(vsgroup, ipAudios.Primary, Level.Audio1, FlowType.Blue);
        AssignFlowToVirtualSignalGroup(vsgroup, ipAudios.Secondary, Level.Audio1, FlowType.Red);

        CreateOrUpdateVSGroup(vsgroup);
        return vsgroup;
    }

    private Resource GetResource(IDmsElement element, string key, DomInstance inputVSGroup, DomInstance outputVSGroup)
    {
        var resource = new Resource
        {
            Name = $"{element.Name} {key}",
            DmaID = element.DmsElementId.AgentId,
            ElementID = element.DmsElementId.ElementId,
            Mode = (inputVSGroup == null || outputVSGroup == null) ? ResourceMode.Unavailable : ResourceMode.Available,
            MaxConcurrency = 1000,
            PoolGUIDs = new List<Guid> { ResourcePool.ID },
            Properties = new List<ResourceManagerProperty>
            {
                new ResourceManagerProperty
                {
                    Name = "Path",
                    Value = key,
                },
                new ResourceManagerProperty
                {
                    Name = "input VSGs",
                    Value = inputVSGroup?.ID.Id.ToString(),
                },
                new ResourceManagerProperty
                {
                    Name = "output VSGs",
                    Value = outputVSGroup?.ID.Id.ToString(),
                },
            },
            Capabilities = new List<Skyline.DataMiner.Net.SRM.Capabilities.ResourceCapability>
            {
                new Skyline.DataMiner.Net.SRM.Capabilities.ResourceCapability
                {
                    CapabilityProfileID = LinkedSourceParameter.ID,
                    IsTimeDynamic = true,
                    Value = new CapabilityParameterValue(),
                },
            },
        };

        var currentResource = CurrentResources.Find(r => r.Name == resource.Name);
        if (currentResource != null)
        {
            resource.ID = currentResource.ID;
            resource.GUID = currentResource.GUID;
        }

        return resource;
    }

    private Dictionary<string, DomInstance> GetSdiBidirectionalFlows(NeuronElement neuron)
    {
        var instances = new Dictionary<string, DomInstance>();
        foreach (var key in neuron.GetSdiBidirectionalTableKeys())
        {
            var dcfInterface = neuron.GetDcfInterfaceId(SdiBidirectionalIoTableDcfParameterGroupId, key);
            var instance = GetSdiFlowInstance(neuron.DmsElement, key, dcfInterface);
            CreateOrUpdateFlow(instance);
            instances.Add(key, instance);
        }

        return instances;
    }

    private Dictionary<string, DomInstance> GetSdiFlows(NeuronElement neuron)
    {
        var sdiStaticFlow = GetSdiStaticFlows(neuron);
        var sdiBidirectionalFlows = GetSdiBidirectionalFlows(neuron);
        return sdiStaticFlow.Concat(sdiBidirectionalFlows).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private Dictionary<string, DomInstance> GetSdiStaticFlows(NeuronElement neuron)
    {
        var instances = new Dictionary<string, DomInstance>();
        foreach (var key in neuron.GetSdiStaticIoTableKeys())
        {
            var dcfInterface = neuron.GetDcfInterfaceId(SdiStaticIoTableDcfParameterGroupId, key);
            var instance = GetSdiFlowInstance(neuron.DmsElement, key, dcfInterface);
            CreateOrUpdateFlow(instance);
            instances.Add(key, instance);
        }

        return instances;
    }

    private DomInstance GetSdiVSGroup(IDmsElement element, Dictionary<string, DomInstance> sdiFlows, VideoPathTableRow videoPathTableRow)
    {
        var vsgroup = GenerateVSGroup(element.Name, videoPathTableRow.Key, Role.Destination);
        var numberSdiFlows = sdiFlows.Count;

        var isFlowAssigned = false;
        var mainInput = videoPathTableRow.MainInput;
        if (mainInput > MinDiscreetValueForSdiFlows &&
            mainInput < MaxDiscreetValueForSdiFlows &&
            (mainInput - SdiFlowsOffset) <= numberSdiFlows)
        {
            var flowKey = mainInput - SdiFlowsOffset;
            var flow = sdiFlows[flowKey.ToString()];
            AssignFlowToVirtualSignalGroup(vsgroup, flow, Level.Video, FlowType.Blue);
            isFlowAssigned = true;
        }

        var backupInput = videoPathTableRow.BackupInput;
        if (backupInput > MinDiscreetValueForSdiFlows &&
            backupInput < MaxDiscreetValueForSdiFlows &&
            (backupInput - SdiFlowsOffset) <= numberSdiFlows)
        {
            var flowKey = backupInput - SdiFlowsOffset;
            var flowInstance = sdiFlows[flowKey.ToString()];
            AssignFlowToVirtualSignalGroup(vsgroup, flowInstance, Level.Video, FlowType.Red);
            isFlowAssigned = true;
        }

        if (isFlowAssigned)
        {
            CreateOrUpdateVSGroup(vsgroup);
            return vsgroup;
        }

        return null;
    }
}

internal class IpFlow
{
    internal DomInstance Primary { get; set; }

    internal DomInstance Secondary { get; set; }
}