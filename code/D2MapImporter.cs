﻿using Sandbox;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Editor;
using Editor.MapEditor;
using Editor.MapDoc;
using System.Collections.Generic;
using System.Text.Json;
using System.Diagnostics;
using System.Collections.Concurrent;

public class D2MapHammerImporter : BaseWindow
{
	public static bool _importObjects = true;
	public static bool _instanceObjects = true;
	public static bool _overrideTerrainMats = false;
	public static bool _overrideAllMats = false;
	public static bool _autosetDetail = true;
	public static bool _importLights = false;
	public static bool _importCubemaps = false;

	private NavigationView View { get; set; }
	public static D2MapHammerImporter Instance { get; set; }

	[Menu( "Hammer", "D2 Map Importer/Import D2 Map", "info" )]
	public static void OpenImporter()
	{
		new D2MapHammerImporter();
	}

	public D2MapHammerImporter()
	{
		Instance = this;
		WindowTitle = "Import Options";
		SetWindowIcon( "grid_view" );

		Size = new Vector2( 512, 256 );
		View = new NavigationView( this );
		Layout = Layout.Column();

		CreateUI();
		Show();
	}

	public void CreateUI()
	{
		var toolsList = Layout.Add( new NavigationView( this ), 1 );

		var footer = toolsList.MenuTop.AddColumn();
		footer.Spacing = 10;

		CheckBox importObjects = footer.Add( new CheckBox( "Import Map Objects" ), 2 );
		importObjects.ToolTip = "Uncheck if you just want to import things like cubemaps and lights";
		importObjects.Value = _importObjects;
		importObjects.Clicked = () => _importObjects = importObjects.Value;

		CheckBox createInstances = footer.Add( new CheckBox( "Instance Map Objects" ), 2 );
		createInstances.ToolTip = "Create Instances For Map Objects (Recommended)";
		createInstances.Value = _instanceObjects;
		createInstances.Clicked = () => _instanceObjects = createInstances.Value;

		CheckBox autosetDetail = footer.Add( new CheckBox( "Automatically Set Detail Geometry" ), 2 );
		autosetDetail.ToolTip = "Small Objects Will Automatically Have \"Detail Geoemetry\" Enabled (Recommended)";
		autosetDetail.Value = _autosetDetail;
		autosetDetail.Clicked = () => _autosetDetail = autosetDetail.Value;

		CheckBox importLights = footer.Add( new CheckBox( "Import Lights" ), 2 );
		importLights.ToolTip = "Imports lights. (VERY WIP)\nColors and rotations can/will be wrong!";
		importLights.Value = _importLights;
		importLights.Clicked = () => _importLights = importLights.Value;

		CheckBox importCubemaps = footer.Add( new CheckBox( "Import Cubemaps" ), 2 );
		importCubemaps.ToolTip = "Imports cubemaps (WIP)\nMay need manually adjusted";
		importCubemaps.Value = _importCubemaps;
		importCubemaps.Clicked = () => _importCubemaps = importCubemaps.Value;

		CheckBox overrideTerrain = footer.Add( new CheckBox( "Override Terrain Materials" ), 2 );
		overrideTerrain.ToolTip = "Force Terrain Objects To Use Generic Dev Texture";
		overrideTerrain.Value = _overrideTerrainMats;
		overrideTerrain.Clicked = () => _overrideTerrainMats = overrideTerrain.Value;

		CheckBox overrideAllMats = footer.Add( new CheckBox( "Override All Materials" ), 2 );
		overrideAllMats.ToolTip = "Force All Objects To Use Generic Dev Texture, with a random color :)";
		overrideAllMats.Value = _overrideAllMats;
		overrideAllMats.Clicked = () => _overrideAllMats = overrideAllMats.Value;

		var files = footer.Add( new Button.Primary( "Select Files", "info" ) );
		files.Clicked = () => HammerImporter();
	}

	public static void HammerImporter()
	{
		List<string> mapList = new List<string>();

		var map = Hammer.ActiveMap;
		if ( !map.IsValid() )
		{
			D2MapImporterPopup.Popup( "D2 Map Importer", "You need to have an active map! (File->New)", Color.Red, 2 );
			return;
		}

		//open a file dialog to select cfg files
		var fd = new FileDialog( null );
		fd.SetNameFilter( "*.cfg" );
		fd.Title = "Select D2 Map(s) (Info.cfg)";
		fd.SetFindExistingFiles();

		if ( fd.Execute() )
		{
			mapList = fd.SelectedFiles;
		}

		//if(_importDecals)
			//ImportDecals( mapList ); //Import decals, WIP

		if(_importLights)
			ImportLights( mapList ); //Import lights, WIP

		if ( _importCubemaps )
			ImportCubemaps( mapList ); //Import cubemaps

		// Create a new stopwatch instance
		Stopwatch stopwatch = new Stopwatch();

		// Start the stopwatch
		stopwatch.Start();

		if( _importObjects )
		{
			foreach ( string path in mapList )
			{
				JsonDocument cfg = JsonDocument.Parse( File.ReadAllText( path ) );

				if ( cfg.RootElement.GetProperty( "Instances" ).EnumerateObject().Count() == 0 )
				{
					Log.Info( $"D2 Map Importer: {Path.GetFileNameWithoutExtension( path )} contains no models, skipping" );
					continue;
				}

				MapGroup group = new MapGroup( map );
				group.Name = Path.GetFileNameWithoutExtension( path );
				group.Name = group.Name.Substring( 0, group.Name.Length - 5 ); //removes "_info" from the name

				// Reads each instance (models) and its transforms (position, rotation, scale)
				foreach ( JsonProperty model in cfg.RootElement.GetProperty( "Instances" ).EnumerateObject() )
				{
					string modelName = path.Contains( "Terrain" ) ? model.Name + "_Terrain" : model.Name;
					MapEntity asset = null;
					Editor.MapDoc.MapInstance asset_instance = null;
					MapEntity previous_model = null;
					int i = 0;

					foreach ( JsonElement instance in model.Value.EnumerateArray() )
					{
						//Create the transforms first before we create the entity
						Vector3 position = new Vector3(
							instance.GetProperty( "Translation" )[0].GetSingle() * 39.37f,
							instance.GetProperty( "Translation" )[1].GetSingle() * 39.37f,
							instance.GetProperty( "Translation" )[2].GetSingle() * 39.37f );

						Quaternion quatRot = new Quaternion
						{
							X = instance.GetProperty( "Rotation" )[0].GetSingle(),
							Y = instance.GetProperty( "Rotation" )[1].GetSingle(),
							Z = instance.GetProperty( "Rotation" )[2].GetSingle(),
							W = instance.GetProperty( "Rotation" )[3].GetSingle()
						};

						Vector3 scale = new Vector3( 
							instance.GetProperty( "Scale" )[0].GetSingle(),
							instance.GetProperty( "Scale" )[1].GetSingle(),
							instance.GetProperty( "Scale" )[2].GetSingle());

						if ( previous_model == null ) //Probably a way better way to do this
						{
							asset = new MapEntity( map );
							asset.ClassName = "prop_static";
							asset.Name = modelName + " " + i;
							asset.SetKeyValue( "model", $"models/{modelName}.vmdl" );
							asset.Scale = scale;

							if ( _autosetDetail )
								SetDetailGeometry( asset );

							if( path.Contains( "Dynamics" ) )
								asset.SetKeyValue( "visoccluder", "0" );

							if ( path.Contains( "Terrain" ) && _overrideTerrainMats )
							{
								asset.SetKeyValue( "materialoverride", "materials/dev/reflectivity_50.vmat" );
							}
							else if ( _overrideAllMats )
							{
								asset.SetKeyValue( "materialoverride", "materials/dev/reflectivity_50.vmat" );
								asset.SetKeyValue( "rendercolor", $"{Random.Shared.Int( 255 )} {Random.Shared.Int( 255 )} {Random.Shared.Int( 255 )} 255" );
							}

							if ( model.Value.GetArrayLength() == 1 ) //dont make an instance if theres only 1 of that asset
							{
								asset.Position = position;
								asset.Angles = path.Contains( "Terrain" ) ? new Angles( 0, 0, 0 ) : ToAngles( quatRot );
							}
							else
							{
								if ( _instanceObjects )
								{
									asset_instance = new Editor.MapDoc.MapInstance()
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
						else //Theres probably a much better way of doing this but fuck it
						{
							if ( previous_model.Scale == scale )
							{
								if ( _instanceObjects )
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
								asset.Scale = scale;

								if ( _autosetDetail )
									SetDetailGeometry( asset );

								if ( path.Contains( "Dynamics" ) )
									asset.SetKeyValue( "visoccluder", "0" );

								if ( path.Contains( "Terrain" ) && _overrideTerrainMats )
								{
									asset.SetKeyValue( "materialoverride", "materials/dev/reflectivity_50.vmat" );
								}
								else if ( _overrideAllMats )
								{
									asset.SetKeyValue( "materialoverride", "materials/dev/reflectivity_50.vmat" );
									asset.SetKeyValue( "rendercolor", $"{Random.Shared.Int( 255 )} {Random.Shared.Int( 255 )} {Random.Shared.Int( 255 )} 255" );
								}

								if ( _instanceObjects )
								{
									asset_instance = new Editor.MapDoc.MapInstance()
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
						_ = (model.Value.GetArrayLength() == 1) || (!_instanceObjects) ? asset.Parent = group : asset_instance.Parent = group;
						i++;
					}
				}
			}
		}

		stopwatch.Stop();
		TimeSpan elapsed = stopwatch.Elapsed;

		D2MapImporterPopup.Popup( "D2 Map Importer", $"Imported {mapList.Count} Files In {elapsed.Seconds} Seconds \nPlease save and reload the map.", Color.Green, 4f );
		Instance.Close();
	}

	private static void ImportDecals( List<string> mapList )
	{
		var map = Hammer.ActiveMap;
		MapGroup decalGroup = new MapGroup( map );
		decalGroup.Name = "Decals";

		foreach ( string path in mapList )
		{
			JsonDocument cfg = JsonDocument.Parse( File.ReadAllText( path ) );
			ConcurrentBag<DecalEntry> decals = new ConcurrentBag<DecalEntry>();

			if ( cfg.RootElement.GetProperty( "Decals" ).EnumerateObject().Count() == 0 )
			{
				Log.Info( $"D2 Map Importer: {Path.GetFileNameWithoutExtension( path )} contains no decals, skipping" );
				continue;
			}

			foreach ( var decal in cfg.RootElement.GetProperty( "Decals" ).EnumerateObject() )
			{
				foreach ( var data in decal.Value.EnumerateArray() )
				{
					//Create the transforms first before we create the entity
					Vector3 position = new Vector3(
						data.GetProperty( "Origin" )[0].GetSingle() * 39.37f,
						data.GetProperty( "Origin" )[1].GetSingle() * 39.37f,
						data.GetProperty( "Origin" )[2].GetSingle() * 39.37f );

					Vector3 corner1 = new Vector3( data.GetProperty( "Corner1" )[0].GetSingle(), data.GetProperty( "Corner1" )[1].GetSingle(), data.GetProperty( "Corner1" )[2].GetSingle() );
					Vector3 corner2 = new Vector3( data.GetProperty( "Corner2" )[0].GetSingle(), data.GetProperty( "Corner2" )[1].GetSingle(), data.GetProperty( "Corner2" )[2].GetSingle() );
					
					var tr = Editor.Trace.Ray( corner1 * 39.37f, corner2 * 39.37f ).Run( map.World );

					decals.Add(new DecalEntry //Need to do this since the trace might collide with other decals if they were already placed
					{
						Name = decal.Name,
						Material = $"{data.GetProperty( "Material" ).GetString()}",
						Position = position + tr.Normal * 16.0f,
						Angles = tr.Normal.EulerAngles,
						Scale = data.GetProperty( "Scale" ).GetSingle()
					} );

					//Log.Info( $"{instance["Material"]} {tr.HitPosition}: {tr.Normal.EulerAngles} {tr.MapNode?.Name}" );
				}
			}

			foreach(var decalinfo in decals)
			{
				MapEntity decalEntity = new MapEntity( map );
				decalEntity.Parent = decalGroup;
				decalEntity.ClassName = "info_overlay";
				decalEntity.Name = decalinfo.Name;
				//Need to figure out how to actually scale decals to match the materials/textures width/height, shit stretches out otherwise
				decalEntity.SetKeyValue( "material", $"materials/decal/{decalinfo.Material}_decal.vmat" );
				decalEntity.SetKeyValue( "depth", "10.0" );

				decalEntity.Position = decalinfo.Position;
				decalEntity.Angles = decalinfo.Angles;
			}
		}
	}

	private struct DecalEntry
	{
		public string Name;
		public string Material;
		public Vector3 Position;
		public Angles Angles;
		public float Scale;
	}

	private static void ImportLights( List<string> mapList )
	{
		var map = Hammer.ActiveMap;
		MapGroup lightGroup = new MapGroup( map );
		lightGroup.Name = "Lights";

		foreach ( string path in mapList )
		{
			JsonDocument cfg = JsonDocument.Parse( File.ReadAllText( path ) );

			if ( cfg.RootElement.GetProperty( "Lights" ).EnumerateObject().Count() == 0 )
			{
				Log.Info( $"D2 Map Importer: {Path.GetFileNameWithoutExtension( path )} contains no lights, skipping" );
				continue;
			}

			foreach ( var light in cfg.RootElement.GetProperty( "Lights" ).EnumerateObject() )
			{
				foreach ( var transforms in light.Value.EnumerateArray() )
				{
					//Create the transforms first before we create the entity
					Vector3 position = new Vector3(
						transforms.GetProperty( "Translation" )[0].GetSingle() * 39.37f,
						transforms.GetProperty( "Translation" )[1].GetSingle() * 39.37f,
						transforms.GetProperty( "Translation" )[2].GetSingle() * 39.37f );

					Rotation quatRot = new Rotation
					{
						x = transforms.GetProperty( "Rotation" )[0].GetSingle(),
						y = transforms.GetProperty( "Rotation" )[1].GetSingle(),
						z = transforms.GetProperty( "Rotation" )[2].GetSingle(),
						w = transforms.GetProperty( "Rotation" )[3].GetSingle()
					};

					MapEntity lightEntity = null;
					lightEntity = new MapEntity( map );
					lightEntity.Parent = lightGroup;
					switch( transforms.GetProperty( "Type" ).GetString() )
					{
						case "Area":
							lightEntity.ClassName = "light_rect";
							lightEntity.SetKeyValue( "lightsourcedim1", $"{transforms.GetProperty( "Size" )[0].GetSingle()}" );
							lightEntity.SetKeyValue( "lightsourcedim0", $"{transforms.GetProperty( "Size" )[1].GetSingle()}" );
							break;
						case "Point":
							lightEntity.ClassName = "light_omni";
							break;
						default:
							lightEntity.ClassName = "light_omni";
							break;
					}
					lightEntity.Name = $"{transforms.GetProperty( "Type" ).GetString()}_{light.Name}";

					lightEntity.SetKeyValue( "Color", $"{(int)(transforms.GetProperty( "Color" )[0].GetSingle() * 255)} {(int)(transforms.GetProperty( "Color" )[1].GetSingle() * 255)} {(int)(transforms.GetProperty( "Color" )[2].GetSingle() * 255)} 255" );
					lightEntity.SetKeyValue( "baked_light_indexing", $"0" );
					lightEntity.SetKeyValue( "Range", $"512" );
					lightEntity.SetKeyValue( "Brightness", $"0.65" );

					lightEntity.Position = position;
					//var angles = ToAngles( quatRot );
					//lightEntity.Angles = new Angles( 90, 0, 0 );
					
					//// Convert the angle from degrees to radians
					//float angleRadians = MathF.PI * 90f / 180.0f;

					//// Create the offset quaternion
					//Rotation offsetQuaternion = Quaternion.CreateFromYawPitchRoll( 0, 0, angleRadians );

					//// Apply the offset rotation
					//Rotation rotatedQuaternion = offsetQuaternion * quatRot;

					//lightEntity.Angles = rotatedQuaternion.Angles(); //angles are all fucky
				}
			}
		}
	}

	private static void ImportCubemaps( List<string> mapList )
	{
		var map = Hammer.ActiveMap;
		MapGroup cubemapGroup = new MapGroup( map );
		cubemapGroup.Name = "Cubemaps";

		foreach ( string path in mapList )
		{
			JsonDocument cfg = JsonDocument.Parse( File.ReadAllText( path ) );

			if ( cfg.RootElement.GetProperty( "Cubemaps" ).EnumerateObject().Count() == 0 )
				continue;

			foreach ( var model in cfg.RootElement.GetProperty( "Cubemaps" ).EnumerateObject() )
			{
				string modelName = model.Name;
				MapEntity cubemap = null;

				foreach ( var transforms in model.Value.EnumerateArray() )
				{
					//Create the transforms first before we create the entity
					Vector3 position = new Vector3(
						transforms.GetProperty( "Translation" )[0].GetSingle() * 39.37f,
						transforms.GetProperty( "Translation" )[1].GetSingle() * 39.37f,
						transforms.GetProperty( "Translation" )[2].GetSingle() * 39.37f );

					Quaternion quatRot = new Quaternion
					{
						X = transforms.GetProperty( "Rotation" )[0].GetSingle(),
						Y = transforms.GetProperty( "Rotation" )[1].GetSingle(),
						Z = transforms.GetProperty( "Rotation" )[2].GetSingle(),
						W = transforms.GetProperty( "Rotation" )[3].GetSingle()
					};

					Vector3 scale = new Vector3(
						transforms.GetProperty( "Scale" )[0].GetSingle() * 39.37f,
						transforms.GetProperty( "Scale" )[1].GetSingle() * 39.37f,
						transforms.GetProperty( "Scale" )[2].GetSingle() * 39.37f );

					cubemap = new MapEntity( map );
					cubemap.ClassName = "env_combined_light_probe_volume";
					cubemap.Name = modelName;
					cubemap.SetKeyValue( "targetname", modelName );

					cubemap.Position = position;
					cubemap.Angles = ToAngles( quatRot );
					cubemap.SetKeyValue( "box_mins", $"-{scale.x} -{scale.y} -{scale.z}" );
					cubemap.SetKeyValue( "box_maxs", $"{scale.x} {scale.y} {scale.z}" );

					cubemap.Parent = cubemapGroup;
				}
			}
		}
	}

	private static void SetDetailGeometry(MapEntity asset)
	{
		float detailMaxVolume = MathF.Pow( 128f, 3f );
		if (asset is MapEntity mapEntity)
		{
			Model model = Model.Load( mapEntity.GetKeyValue( "model" ) );
			
			//Log.Info( $"{asset.Name} {model.Bounds.Size.Length}" );
			
			//TODO: Should lightmap scale also be adjusted with model size?
			//if( model.Bounds.Size.Length < 1000) //Problem with this is it takes into account the models origin point for the bounds
			//{
			//	mapEntity.SetKeyValue( "lightmapscalebias", "2" );
			//}
			//if ( model.Bounds.Size.Length > 2000 )
			//{
			//	mapEntity.SetKeyValue( "lightmapscalebias", "-2" );
			//}

			if ( (model.Bounds.Volume * asset.Scale.x * asset.Scale.y * asset.Scale.z) <= detailMaxVolume) //If the model is 'small'
			{
				mapEntity.SetKeyValue( "detailgeometry", "1" );
			}
			else
			{
				mapEntity.SetKeyValue( "visoccluder", "1" ); //Big model = use vis
				mapEntity.SetKeyValue( "detailgeometry", "0" );
			}

			if(asset.Name.Contains("Terrain"))
			{
				mapEntity.SetKeyValue( "visoccluder", "1" );
				mapEntity.SetKeyValue( "disablemeshmerging", "1" );
			}
		}
	}

	//Converts a Quaternion to Euler Angles + some fuckery to fix certain rotations
	private static Angles ToAngles( Quaternion q )
	{
		float SINGULARITY_THRESHOLD = 0.4999995f;
		float SingularityTest = q.Z * q.X - q.W * q.Y;
		
		float num = 2f * q.W * q.W + 2f * q.X * q.X - 1f;
		float num2 = 2f * q.X * q.Y + 2f * q.W * q.Z;
		float num3 = 2f * q.X * q.Z - 2f * q.W * q.Y;
		float num4 = 2f * q.Y * q.Z + 2f * q.W * q.X;
		float num5 = 2f * q.W * q.W + 2f * q.Z * q.Z - 1f;
		Angles result = default;

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


	[Menu( "Hammer", "D2 Map Importer/Help", "info" )]
	private static void OpenHelp()
	{
		Process.Start( new ProcessStartInfo { FileName = "https://github.com/DeltaDesigns/SBox-Destiny-2-Map-Importer", UseShellExecute = true } );
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
