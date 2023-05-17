This document is intended to show you the Werkr Agent Windows MSI installer process and highlight key details.

To get started download the appropriate MSI file from the [github releases](https://github.com/DarkgreyDevelopment/Werkr.Agent/releases/tag/latest) page. Note that if you're not sure which msi file to download then you probably want the x64 version.

<br/>

# Msi Installation

1. Double Click the Msi installer and select Next.  
![WerkrAgentIntro](../../images/articles/HowTo/WindowsAgentInstall/0-WerkrAgentIntro.png "\"Werkr Agent Setup\" Msi \"Welcome\" Menu. Progress buttons are \"Back\", \"Next\", and \"Cancel\". The \"Back\" button is disabled and cannot be clicked.")

<br/>

2. Specfy agent settings.  
![AgentOptions](../../images/articles/HowTo/WindowsAgentInstall/1-AgentOptions.png "\"Werkr Agent Options\" Msi Menu. Options for selection are \"Agent Working Directory\", which has a textbox with a prepopulated value of \"C:\\\" and a \"browse\" button next to it. Next is \"Enable Agent to use PowerShell\" with \"True\" or \"False\" options. After that is \"Enable Agent to use System Shell (cmd)\" with \"True\" or \"False\" options. The last option is the \"Allowed Hosts\" textbox, this has a pre-populated value of \"*\".  Available progress buttons are \"Back\", \"Next\", and \"Cancel\".")

* The `Agent Working Directory` setting determines the default path for scripts and commands to start from.
* The `Enable Agent to use PowerShell` setting determines whether the agent will enable the built in PowerShell host & its associated grpc communications channel.
* The `Enable Agent to use System Shell  (cmd)` setting determines whether the agent will enable the built in command process host & its associated grpc communications channel.
* Note that if you turn both PowerShell and the System Shell services off then the agent will only be able to perform System Defined actions.
* The `Allowed Hosts` settings determines which hosts are allowed to communicate with the agent.
  * Leave this as `*` to enable all outside servers to communicate with this agent.
  * This list is semi-colon delimited. Ex: `example.com;localhost;192.168.1.16`

<br/>

3. Select your "Kestrel Certificate Config" type from the dropdown.  
![SelectCertificateType](../../images/articles/HowTo/WindowsAgentInstall/2-SelectCertificateType.png "\"Werkr Agent Certificate Options\" Msi Menu. There is a single dropdown visible at the top for the \"Kestrel Certificate Config\" option that has a default value of \"(Select)\". Available progress buttons are \"Back\", \"Next\", and \"Cancel\". The \"Next\" button is disabled and cannot be clicked.")
* Regardless of which certificate type you choose, you will need to enter the `Certificate Url` that you want the agent to listen on.
  * Once populated, You may need to click out of the Certificate Url field for the "Next" button to become enabled.


<ul>

<details>
  <summary>CertStore (expand)</summary>

  ![CertStore](../../images/articles/HowTo/WindowsAgentInstall/3-CertStore.png "\"Werkr Agent Certificate Options\" Msi Menu. At the top the \"Kestrel Certificate Config\" dropdown has the \"CERTSTORE\" option selected. The second option is the \"Certificate Url\" textbox. The third option is the \"Certificate Subject\" textbox. The fourth option is the \"Certificate Store\" textbox. The last option is the \"Certificate Location\" textbox. Available progress buttons are \"Back\", \"Next\", and \"Cancel\". The \"Next\" button is disabled and cannot be clicked. There is also a \"Browse\" button in the bottom left menu area.")  
  If you know your certificates store information then you can feel free to paste it into the fields.  
  Otherwise select the browse button on the bottom left and you can select the appropriate certificate from the ones availabe in the store.  

  ![CertStore_Selection](../../images/articles/HowTo/WindowsSharedInstall/CertStore_Selection.png "An example of the certstore selection menu that appears when you select the CertStore \"Browse\" button. This menu shows a list of certificate details from all of the accessible certificate stores. It shows the \"Store\", \"Location\", \"SimpleName\", \"Subject\" and \"SignatureAlgorithm\" fields for each certificate. Available progress buttons are \"OK\" and \"Cancel\". By default the first entry in the list is selected.")

</details>

</ul>

<ul>

<details>
  <summary>CertFile (expand)</summary>

  ![CertFile](../../images/articles/HowTo/WindowsAgentInstall/3-CertFile.png "\"Werkr Agent Certificate Options\" Msi Menu. At the top the \"Kestrel Certificate Config\" dropdown has the \"CERTFILE\" option selected. The second option is the \"Certificate Url\" textbox. The third option is the \"Certificate Path\" textbox, which has a \"Browse\" button next to it. The last option is the \"Certificate Password\" textbox. Available progress buttons are \"Back\", \"Next\", and \"Cancel\". The \"Next\" button is disabled and cannot be clicked.")  
  ![FileBrowse](../../images/articles/HowTo/WindowsSharedInstall/FileBrowse.png "An example of the \"Select a certificate file\" file selection menu that appears when you select the CertFile \"Browse\" button. It is a standard windows file selection dialog and is pre-populated to search for a \"Certificate File\" (.pfx) file.")  

</details>

</ul>

<ul>

<details>
  <summary>CertAndKeyFile (expand)</summary>

  ![CertAndKeyFile](../../images/articles/HowTo/WindowsAgentInstall/3-CertAndKeyFile.png "\"Werkr Agent Certificate Options\" Msi Menu. At the top the \"Kestrel Certificate Config\" dropdown has the \"CERTANDKEYFILE\" option selected. The second option is the \"Certificate Url\" textbox. The third option is the \"Certificate Path\" textbox, which has a \"Browse\" button next to it. The fourth option is the \"Certificate Password\" textbox. The last option is a \"KeyFile Path\" textbox, which has a browse button next to it. Available progress buttons are \"Back\", \"Next\", and \"Cancel\". The \"Next\" button is disabled and cannot be clicked.")  

  ![FileBrowse](../../images/articles/HowTo/WindowsSharedInstall/FileBrowse.png "An example of the \"Select a certificate file\" file selection menu that appears when you select the Certificate Path \"Browse\" button. It is a standard windows file selection dialog and is pre-populated to search for a \"Certificate File\" (.pfx) file. This image is also used as an example for the \"Select a certificate key file\" menu that appears when you select the KeyFile Path \"Browse\" button. The only difference between those two menus is the title bar and the pre-populate search extension (.key instead of .pfx).")  

</details>

</ul>

<br/>

4. Specify logging levels. It is suggested that you leave these at their default values unless you have a specific reason to change them.  
![Logging](../../images/articles/HowTo/WindowsAgentInstall/4-Logging.png "\"Werkr Agent Logging Options\" Msi Menu. At the top there is a \"Default Application LogLevel\" dropdown has the \"Trace\" option selected. The second option is the \"Hosting Lifetime LogLevel\" drop which has the \"Error\" option selected. The last option is the \"AspNetCore LogLevel\" textbox which has the \"Error\" option selected. Available progress buttons are \"Back\", \"Next\", and \"Cancel\".")  
The Werkr project utilizes the Microsoft.Extensions.Logging package and uses "[LogLevel](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.logging.loglevel)" to determine what to output to the log. See the microsoft article [Logging in C# and .Net](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging) for more details.

<br/>

5. Select Install Path - You can choose any location you want the application to be installed at.  
![DestinationPath](../../images/articles/HowTo/WindowsAgentInstall/5-DestinationPath.png "\"Werkr Agent Setup\" Msi \"Destination Folder\" Menu. There is a textbox with a prepopulated path and a \"Change\" button below it. Progress buttons are \"Back\", \"Next\", and \"Cancel\".")

<br/>

6. Select Install  
![InstallButton](../../images/articles/HowTo/WindowsAgentInstall/6-InstallButton.png "\"Werkr Agent Setup\" Msi \"Ready to install\" Menu. Progress buttons are \"Back\", \"Install\", and \"Cancel\".")  

The installer will now:
  * Extract the portable application files
  * Populate the appsettings file with the settings you selected
  * Register the application as a windows service
  * Register installation details with windows to allow for a simple uninstall.

<br/>

7. Installation Complete, Select Finish!  
![FinishButton](../../images/articles/HowTo/WindowsAgentInstall/7-FinishButton.png "\"Werkr Agent Setup\" Msi \"Completed\" Menu. Progress buttons are \"Back\", \"Finish\", and \"Cancel\". The \"Back\" and \"Cancel\" buttons are disabled and cannot be clicked.")

<br/><br/>

# Post Install & Removal

After installation you can find the application registered under the `Programs and Features` control panel menu, as well as under the `Installed Apps` menu.

The application has also been registered as a windows service.

<ul>

<details>
  <summary>Service Info (expand)</summary>

  ![ServiceInfo](../../images/articles/HowTo/WindowsAgentInstall/PostInstall-ServiceInfo.png "An example of the Windows services.msc menu. It shows the \"Werkr.Agent\" service available and ready to start.")  
  Interact with the service (start/stop/disable) via the Windows Services mmc snapin.  

</details>

</ul>

<br/>

## Msi Removal:

The application can be removed by selecting the `Uninstall` button from either the `Programs and Features` or `Installed Apps` menus.

<ul>

<details>
  <summary>Programs and Features (expand)</summary>

  ![ProgramsAndFeatures](../../images/articles/HowTo/WindowsAgentInstall/PostInstall-ProgramsAndFeatures.png "An example of the Windows Programs and Features menu showing the \"Werkr Agent\" application installed.")

</details>

</ul>

<ul>

<details>
  <summary>Installed Apps (expand)</summary>

  ![InstalledApps](../../images/articles/HowTo/WindowsAgentInstall/PostInstall-InstalledApps.png "An example of the Windows Installed Apps menu that shows the \"Werkr Agent\" application installed.")  
  The `uninstall` button in this menu is hidden until you select the elipses menu on the right side of the screen.

</details>

</ul>

<br/>

Please note that after uninstalling the application you may still have a `Werkr Agent` directory in the install location.  
![RemainingFiles](../../images/articles/HowTo/WindowsSharedUninstall/RemainingFiles.png "A partial snippet of a windows explorer menu that shows the \"Werkr Agent\" and \"Werkr Server\" directories still existing post un-install.")  
This directory should only contain leftover log files that were generated by the application during its operation.  
You can feel free to delete this directory and its contents after the uninstall wizard has completed successfully.  


<br/>


## Portable Removal
If you downloaded the portable version of the application then it can be "uninstalled" simply by deleting the folder that has the application files.

</br>
