// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Resources.Models;
using Azure.ResourceManager.Samples.Common;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager;
using System.Reflection;
using System.Threading.Tasks;
using System;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;

namespace ManageIPAddress
{
    public class Program
    {
        private static ResourceIdentifier? _resourceGroupId = null;
        private static readonly string UserName = Utilities.CreateUsername();
        private static readonly string Password = Utilities.CreatePassword();

        /**
         * Azure Network sample for managing IP address -
         *  - Assign a public IP address for a virtual machine during its creation
         *  - Assign a public IP address for a virtual machine through an virtual machine update action
         *  - Get the associated public IP address for a virtual machine
         *  - Get the assigned public IP address for a virtual machine
         *  - Remove a public IP address from a virtual machine.
         */
        public static async Task RunSample(ArmClient client)
        {
            string publicIPAddressName1 = Utilities.CreateRandomName("pip1-");
            string publicIPAddressName2 = Utilities.CreateRandomName("pip2-");
            string publicIPAddressLeafDNS1 = Utilities.CreateRandomName("dns-pip1-");
            string publicIPAddressLeafDNS2 = Utilities.CreateRandomName("dns-pip2-");
            string vmName = Utilities.CreateRandomName("vm");

            try
            {
                // Get default subscription
                SubscriptionResource subscription = await client.GetDefaultSubscriptionAsync();

                // Create a resource group in the EastUS region
                string rgName = Utilities.CreateRandomName("CosmosDBTemplateRG");
                Utilities.Log($"creating resource group with name:{rgName}");
                ArmOperation<ResourceGroupResource> rgLro = await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, rgName, new ResourceGroupData(AzureLocation.EastUS));
                ResourceGroupResource resourceGroup = rgLro.Value;
                _resourceGroupId = resourceGroup.Id;
                Utilities.Log("Created a resource group with name: " + resourceGroup.Data.Name);

                //============================================================
                // Assign a public IP address for a VM during its creation

                // Define a public IP address to be used during VM creation time

                Utilities.Log("Creating a public IP address...");

                PublicIPAddressData publicIPInput = new PublicIPAddressData()
                {
                    Location = resourceGroup.Data.Location,
                    PublicIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                    DnsSettings = new PublicIPAddressDnsSettings()
                    {
                        DomainNameLabel = publicIPAddressLeafDNS1
                    }
                };
                var publicIPLro = await resourceGroup.GetPublicIPAddresses().CreateOrUpdateAsync(WaitUntil.Completed, publicIPAddressName1, publicIPInput);
                PublicIPAddressResource publicIP = publicIPLro.Value;

                //var publicIPAddress = azure.PublicIPAddresses.Define(publicIPAddressName1)
                //        .WithRegion(Region.USEast)
                //        .WithNewResourceGroup(rgName)
                //        .WithLeafDomainLabel(publicIPAddressLeafDNS1)
                //        .Create();

                Utilities.Log($"Created a public IP address: {publicIP.Data.Name}");

                // Use the pre-created public IP for the new VM

                Utilities.Log("Creating a Windows VM");

                var t1 = DateTime.UtcNow;

                var vm = azure.VirtualMachines.Define(vmName)
                        .WithRegion(Region.USEast)
                        .WithExistingResourceGroup(rgName)
                        .WithNewPrimaryNetwork("10.0.0.0/28")
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithExistingPrimaryPublicIPAddress(publicIPAddress)
                        .WithPopularWindowsImage(KnownWindowsVirtualMachineImage.WindowsServer2012R2Datacenter)
                        .WithAdminUsername(UserName)
                        .WithAdminPassword(Password)
                        .WithSize(VirtualMachineSizeTypes.Parse("Standard_D2a_v4"))
                        .Create();

                var t2 = DateTime.UtcNow;
                Utilities.Log("Created VM: (took "
                        + (t2 - t1).TotalSeconds + " seconds) " + vm.Id);
                // Print virtual machine details
                Utilities.PrintVirtualMachine(vm);

                //============================================================
                // Gets the public IP address associated with the VM's primary NIC

                Utilities.Log("Public IP address associated with the VM's primary NIC [After create]");
                // Print the public IP address details
                Utilities.PrintIPAddress(vm.GetPrimaryPublicIPAddress());

                //============================================================
                // Assign a new public IP address for the VM

                // Define a new public IP address

                var publicIPAddress2 = azure.PublicIPAddresses.Define(publicIPAddressName2)
                        .WithRegion(Region.USEast)
                        .WithNewResourceGroup(rgName)
                        .WithLeafDomainLabel(publicIPAddressLeafDNS2)
                        .Create();

                // Update VM's primary NIC to use the new public IP address

                Utilities.Log("Updating the VM's primary NIC with new public IP address");

                var primaryNetworkInterface = vm.GetPrimaryNetworkInterface();
                primaryNetworkInterface.Update()
                        .WithExistingPrimaryPublicIPAddress(publicIPAddress2)
                        .Apply();

                //============================================================
                // Gets the updated public IP address associated with the VM

                // Get the associated public IP address for a virtual machine
                Utilities.Log("Public IP address associated with the VM's primary NIC [After Update]");
                vm.Refresh();
                Utilities.PrintIPAddress(vm.GetPrimaryPublicIPAddress());

                //============================================================
                // Remove public IP associated with the VM

                Utilities.Log("Removing public IP address associated with the VM");
                vm.Refresh();
                primaryNetworkInterface = vm.GetPrimaryNetworkInterface();
                publicIPAddress = primaryNetworkInterface.PrimaryIPConfiguration.GetPublicIPAddress();
                primaryNetworkInterface.Update()
                        .WithoutPrimaryPublicIPAddress()
                        .Apply();

                Utilities.Log("Removed public IP address associated with the VM");

                //============================================================
                // Delete the public ip
                Utilities.Log("Deleting the public IP address");
                azure.PublicIPAddresses.DeleteById(publicIPAddress.Id);
                Utilities.Log("Deleted the public IP address");
            }
            finally
            {
                try
                {
                    if (_resourceGroupId is not null)
                    {
                        Utilities.Log($"Deleting Resource Group: {_resourceGroupId}");
                        await client.GetResourceGroupResource(_resourceGroupId).DeleteAsync(WaitUntil.Completed);
                        Utilities.Log($"Deleted Resource Group: {_resourceGroupId}");
                    }
                }
                catch (NullReferenceException)
                {
                    Utilities.Log("Did not create any resources in Azure. No clean up is necessary");
                }
                catch (Exception ex)
                {
                    Utilities.Log(ex);
                }
            }
        }

        public static async Task Main(string[] args)
        {
            try
            {
                //=================================================================
                // Authenticate
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);

                await RunSample(client);
            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
            }
        }
    }
}