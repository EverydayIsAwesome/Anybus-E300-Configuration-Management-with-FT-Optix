Step-by-Step Guide: Managing E300 Configuration via FT Optix & Anybus E300 Modbus TCP

Here is the how-to video: https://youtu.be/Xw5f-RCL9Nk

Pre-requisites:
FT Optix Software Installed
If you don't have it, contact your Rockwell sales representative to get access to the download link: https://www.rockwellautomation.com/en-us/products/software/factorytalk/optix.html 
Ensure the firmware version is 1.006 build 1 or later.
Only from this firmware onwards are all Ethernet/IP objects available in the Modbus TCP module.

Step 1: Open the Sample Project
Locate the FT Optix sample project file you downloaded.
Double-click to open it in FT Optix.

Step 2: Set the Correct Recipe File
Inside FT Optix, ensure the Recipe File points to the correct path.
This is crucial; without this, the configuration won't work properly.

Step 3: Connect to Anybus E300 Communication Module
Make sure your PC is connected to the E300 communication module.
Check the firmware (must be 1.006 build 1 or later).
Set the IP Address in the project to match the IP of your E300 module:
Go to the relevant field and adjust if necessary.

Step 4: Check Live Communication
In FT Optix, verify communication:
If connection is active, fields won’t show red highlights (indicating successful communication).

Step 5: View/Modify FLA Setting
Navigate to Over Trip Settings → Locate FLA (Full Load Amps) setting.
You can view current values and modify as required.

Step 6: Save Configuration Recipe
Once finished configuring:
Go to Recipe Editor → Save.
Give the recipe a name (e.g., Size6007).
IMPORTANT: After typing the name, hit ENTER before clicking Save.

Step 7: Export Recipe to .CSV File
Export your recipe to a .csv file.
Navigate to the project’s file directory and confirm your recipe has been saved there.
You can now:
Open the .csv file.
Add more recipes in.
Save changes.

Step 8: Import Modified Recipe
Back in FT Optix:
Go to Import → Select the modified .csv file.
Issue: Sometimes imported recipes won’t immediately show up due to a software bug.

Step 9: Fix Recipe Display Bug
Save a new dummy recipe:
Give it a unique name (not duplicating existing ones).
Hit ENTER, then Save.
Now check the recipe dropdown; your imported recipe should appear!

Step 10: Apply New Recipe
Select the imported recipe from the dropdown.
Click Apply to send settings to the E300 module.
Check if FLA settings and other parameters have updated.

Step 11: (Optional) Add More Modbus Tags
In the Modbus TCP module section:
You can add or modify tags (the project includes configuration parameters only; read-only parameters are not included).
To add monitoring parameters:
Edit the modbus-tab.csv file.
Update tags as needed.

Step 12: Log Out & Finish
Once done, log out of FT Optix.
You’ve now successfully managed your E300 configuration!

Support Contacts:
For issues with Anybus E300 Modbus TCP module, contact:
HMS Network Support Team (via their support portal or office).
For issues specific to E300 Protection Relay, reach out to:
Rockwell Automation Support Team.
