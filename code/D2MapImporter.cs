﻿using Sandbox;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Editor;
using Editor.Graphic;
using Editor.MapEditor;
using Editor.MapDoc;
using System.Collections.Generic;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using Sandbox.Utility;

public class D2MapHammerImporter : BaseWindow
{
	public static bool _instanceObjects = true;
	public static bool _overrideTerrainMats = false;
	public static bool _autosetDetail = true;

	NavigationView View;
	public static D2MapHammerImporter Instance { get; set; }

	public D2MapHammerImporter()
	{
		Instance = this;
		WindowTitle = "Import Options";
		SetWindowIcon( "grid_view" );

		Size = new Vector2( 512, 256 );
		View = new NavigationView( this );

		SetLayout( LayoutMode.LeftToRight );
		//Layout.Add( View, 1 );

		CreateUI();
		Show();
	}

	public void CreateUI()
	{
		var toolsList = Layout.Add( new NavigationView( this ), 1 );

		//var options = toolsList.AddPage( "Options", "hardware" );

		var footer = toolsList.MenuTop.AddColumn();
		footer.Spacing = 10;

		CheckBox createInstances = footer.Add( new CheckBox( "Instance Map Objects" ), 2 );
		createInstances.ToolTip = "Create Instances For Map Objects, Improves Performance (Recommended)";
		createInstances.Value = _instanceObjects;
		createInstances.Clicked = () => _instanceObjects = createInstances.Value;

		CheckBox overrideTerrain = footer.Add( new CheckBox( "Override Terrain Materials" ), 2 );
		overrideTerrain.ToolTip = "Force Terrain Objects To Use Generic Dev Texture";
		overrideTerrain.Value = _overrideTerrainMats;
		overrideTerrain.Clicked = () => _overrideTerrainMats = overrideTerrain.Value;

		CheckBox autosetDetail = footer.Add( new CheckBox( "Automatically Set Detail Geometry" ), 2 );
		autosetDetail.ToolTip = "Small Objects Will Automatically Have \"Detail Geoemetry\" Enabled, Improves Performance (Recommended)";
		autosetDetail.Value = _autosetDetail;
		autosetDetail.Clicked = () => _autosetDetail = autosetDetail.Value;

		var files = footer.Add( new Button.Primary( "Select Files", "info" ) );
		files.Clicked = () => HammerImporter();
	}

	[Menu( "Hammer", "D2 Map Importer/Import D2 Map", "info" )]
	public static void OpenImporter()
	{
		_ = new D2MapHammerImporter();
	}

	//[Menu( "Hammer", "D2 Map Importer/Import D2 Map", "info" )]
	public static void HammerImporter()
	{
		List<string> mapList = new List<string>();
		
		var map = Hammer.ActiveMap;
		if ( !map.IsValid() )
		{
			D2MapImporterPopup.Popup( "D2 Map Importer", "You need to have an active map! (File->New)", Color.Red, 2 );	
			return;
		}

		//open a file dialog to select the cfg file
		//Popup( "D2 Map Importer", $"Find and select the map files (.cfg) to import", Color.Blue, 1 );
		
		var fd = new FileDialog( null );
		fd.SetNameFilter("*.cfg");
		fd.Title = "Select D2 Map(s) (Info.cfg)";
		fd.SetFindExistingFiles();

		if ( fd.Execute() )
		{
			mapList = fd.SelectedFiles;
		}

		// Create a new stopwatch instance
		Stopwatch stopwatch = new Stopwatch();

		// Start the stopwatch
		stopwatch.Start();

		foreach ( string path in mapList)
		{
			JsonNode cfg = JsonNode.Parse( File.ReadAllText( path ) );

			if( cfg["Instances"].AsObject().Count == 0 )
			{
				Log.Info( $"D2 Map Importer: {Path.GetFileNameWithoutExtension( path )} contains no models, skipping" );
				continue;
			}
			
			MapGroup group = new MapGroup( map );
			group.Name = Path.GetFileNameWithoutExtension( path );
			group.Name = group.Name.Substring( 0, group.Name.Length - 5 ); //removes "_info" from the name

			// Reads each instance (models) and its transforms (position, rotation, scale)
			foreach ( var model in (JsonObject)cfg["Instances"] )
			{
				string modelName = path.Contains("Terrain") ? model.Key + "_Terrain" : model.Key;
				MapEntity asset = null;
				MapInstance asset_instance = null;
				MapEntity previous_model = null;
				int i = 0;

				foreach ( var instance in (JsonArray)model.Value )
				{
					//Create the transforms first before we create the entity
					Vector3 position = new Vector3( (float)instance["Translation"][0] * 39.37f, (float)instance["Translation"][1] * 39.37f, (float)instance["Translation"][2] * 39.37f );

					Quaternion quatRot = new Quaternion
					{
						X = (float)instance["Rotation"][0],
						Y = (float)instance["Rotation"][1],
						Z = (float)instance["Rotation"][2],
						W = (float)instance["Rotation"][3]
					};

					if ( previous_model == null ) //Probably a way better way to do this
					{ 
						asset = new MapEntity( map );

						asset.ClassName = "prop_static";
						asset.Name = modelName + " " + i;
						asset.SetKeyValue( "model", $"models/{modelName}.vmdl" );
						
						if( _autosetDetail )
							SetDetailGeometry( asset );

						//asset.SetKeyValue( "detailgeometry", path.Contains( "Dynamics" ) ? "1" : "0" );
						asset.SetKeyValue( "visoccluder", path.Contains( "Dynamics" ) ? "0" : "1" );
						asset.Scale = new Vector3( (float)instance["Scale"] );

						if ( path.Contains( "Terrain" ) && _overrideTerrainMats )
						{
							asset.SetKeyValue( "materialoverride", "materials/dev/reflectivity_50.vmat" );
						}

						if ( model.Value.AsArray().Count == 1 ) //dont make an instance if theres only 1 of that asset
						{
							asset.Position = position;
							asset.Angles = path.Contains( "Terrain" ) ? new Angles( 0, 0, 0 ) : ToAngles( quatRot );
						}
						else
						{
							if( _instanceObjects )
							{
								asset_instance = new MapInstance()
								{
									Target = asset,
									Position = position,
									Angles = path.Contains( "Terrain" ) ? new Angles( 0, 0, 0 ) : ToAngles( quatRot ),
									Name = asset.Name
								};
							}
							else
							{
								asset.Position = position;
								asset.Angles = path.Contains( "Terrain" ) ? new Angles( 0, 0, 0 ) : ToAngles( quatRot );
							}	
						}
						
						previous_model = asset;
					}
					else
					{
						if ( previous_model.Scale == (float)instance["Scale"] )
						{
							if(_instanceObjects)
							{
								asset_instance.Copy();
								asset_instance.Angles = ToAngles( quatRot );
								asset_instance.Position = position;
							}
							else
							{
								asset.Copy();
								asset.Angles = ToAngles( quatRot );
								asset.Position = position;
							}	
						}
						else
						{
							asset = new MapEntity( map );

							asset.ClassName = "prop_static";
							asset.Name = modelName + " " + i;
							asset.SetKeyValue( "model", $"models/{modelName}.vmdl" );

							if ( _autosetDetail )
								SetDetailGeometry( asset );

							//asset.SetKeyValue( "detailgeometry", path.Contains( "Dynamics" ) ? "1" : "0" );
							asset.SetKeyValue( "visoccluder", path.Contains( "Dynamics" ) ? "0" : "1" );
							asset.Scale = new Vector3( (float)instance["Scale"] );

							if(path.Contains("Terrain") && _overrideTerrainMats )
							{
								asset.SetKeyValue( "materialoverride", "materials/dev/reflectivity_50.vmat" );
							}

							if ( _instanceObjects )
							{
								asset_instance = new MapInstance()
								{
									Target = asset,
									Position = position,
									Angles = path.Contains( "Terrain" ) ? new Angles( 0, 0, 0 ) : ToAngles( quatRot ),
									Name = asset.Name
								};
							}
							else
							{
								asset.Position = position;
								asset.Angles = path.Contains( "Terrain" ) ? new Angles( 0, 0, 0 ) : ToAngles( quatRot );
							}

							previous_model = asset;
						}
					}
					_ = (model.Value.AsArray().Count == 1) || (!_instanceObjects) ? asset.Parent = group : asset_instance.Parent = group;
					i++;
				}
			}
		}

		stopwatch.Stop();
		TimeSpan elapsed = stopwatch.Elapsed;

		D2MapImporterPopup.Popup( "D2 Map Importer", $"Imported {mapList.Count} Files In {elapsed.Seconds} Seconds \nPlease save and reload the map.", Color.Green, 2.75f );
		Instance.Close();
	}

	[Menu( "Hammer", "D2 Map Importer/Help", "info" )]
	private static void OpenHelp()
	{
		Process.Start( new ProcessStartInfo { FileName = "https://github.com/DeltaDesigns/SBox-Destiny-2-Map-Importer", UseShellExecute = true } );
	}

	private static void SetDetailGeometry(MapEntity asset)
	{
		float detailMaxVolume = MathF.Pow( 128f, 3f );
		if (asset is MapEntity mapEntity)
		{
			Model model = Model.Load( mapEntity.GetKeyValue( "model" ) );

			if( (model.Bounds.Volume * asset.Scale.x * asset.Scale.y * asset.Scale.z) <= detailMaxVolume)
			{
				mapEntity.SetKeyValue( "detailgeometry", "1" );
			}
			else
			{
				mapEntity.SetKeyValue( "detailgeometry", "0" );
			}
		}
	}

	//Converts a Quaternion to Euler Angles + some fuckery to fix certain rotations
	private static Angles ToAngles( Quaternion q, string model = "" )
	{
		float SINGULARITY_THRESHOLD = 0.4999995f;
		float SingularityTest = q.Z * q.X - q.W * q.Y;
		
		float num = 2f * q.W * q.W + 2f * q.X * q.X - 1f;
		float num2 = 2f * q.X * q.Y + 2f * q.W * q.Z;
		float num3 = 2f * q.X * q.Z - 2f * q.W * q.Y;
		float num4 = 2f * q.Y * q.Z + 2f * q.W * q.X;
		float num5 = 2f * q.W * q.W + 2f * q.Z * q.Z - 1f;
		Angles result = default( Angles );

		if ( SingularityTest < -SINGULARITY_THRESHOLD)
		{
			result.pitch = 90f;
			result.yaw = MathF.Atan2( q.W, q.X ).RadianToDegree()-90;
			result.roll = MathF.Atan2( q.Y, q.Z ).RadianToDegree()-90;
		}
		else if ( SingularityTest > SINGULARITY_THRESHOLD )
		{
			result.pitch = -90f;
			result.yaw = -MathF.Atan2( q.W, q.X ).RadianToDegree() + 90;
			result.roll = MathF.Atan2( q.Y, q.Z ).RadianToDegree() + 90;
		}
		else
		{
			result.pitch = MathF.Asin( 0 - num3 ).RadianToDegree();
			result.yaw = MathF.Atan2( num2, num ).RadianToDegree();
			result.roll = MathF.Atan2( num4, num5 ).RadianToDegree();
		}
		
		return new Angles( result.pitch, result.yaw, result.roll );
	}
}

public class D2MapImporterPopup : NoticeWidget
{
	public static float popupTime = 3;
	public D2MapImporterPopup()
	{

	}

	public static void Popup( string title, string subtitle, Color color, float time = 1 )
	{
		var notice = new D2MapImporterPopup();
		notice.Title = title;
		notice.Subtitle = subtitle;
		notice.BorderColor = color;
		notice.Icon = "warning";
		popupTime = time;
		notice.Reset();
	}

	public override void Reset()
	{
		base.Reset();

		SetBodyWidget( null );
		FixedWidth = 320;
		FixedHeight = 80;
		Visible = true;
		IsRunning = true;
		NoticeManager.Remove( this, popupTime );
	}
}
