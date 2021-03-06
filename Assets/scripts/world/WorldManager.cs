using UnityEngine;
using KBEngine;
using System.Collections;
using System;
using System.Xml;
using System.Collections.Generic;

public class WorldObjectLoadCB
{
	public virtual void onWorldObjectLoadCB(UnityEngine.Object obj)
	{
	}
}

public class WorldSceneObject : SceneObject
{
	public override void onAssetAsyncLoadObjectCB(string name, UnityEngine.Object obj, Asset asset)
	{
		base.onAssetAsyncLoadObjectCB(name, obj, asset);
		Scene scene = loader.inst.findScene(loader.inst.currentSceneName, false);
		if(scene == null && scene.worldmgr != null)
		{
			Common.ERROR_MSG("WorldSceneObject::onAssetAsyncLoadObjectCB: not found scene! name=" + asset.source);
		}
		else
		{
			if(scene.worldmgr.worldObjectLoadCB != null)
			{
				scene.worldmgr.worldObjectLoadCB.onWorldObjectLoadCB(obj);
			}
		}
	}
}

public class WorldManager : AssetAsyncLoadObjectCB
{
	public static WorldManager currinst = null;
	
	public struct ChunkPos
	{
		public int x;
		public int y;
	};
	
	public WorldObjectLoadCB worldObjectLoadCB = null;
	
	public static Shader default_shader_diffuse = null;
	
	public Vector3 size = new Vector3();
	public List<string> load_treePrototypes = new List<string>();
	public List<KeyValuePair<string, string>> load_splatPrototypes = new List<KeyValuePair<string, string>>();
	public List<KeyValuePair<Vector2, Vector2>> splatPrototypes_titlesizeoffset = new List<KeyValuePair<Vector2, Vector2>>();
	public List<string> load_detailPrototypes = new List<string>();

	public UnityEngine.GameObject[] treePrototypes = null;
	public Texture2D[,] splatPrototypes = null;
	public Texture2D[] detailPrototypes = null;
	
	public string name;
	public string terrainName;
	public int chunkSplit = 0;
	public float chunkSize = 0f;
	
	public UnityEngine.GameObject[,,] terrainObjs = null;
	public Asset[,,] terrainAssetBundles = null;
	
	public bool[,,] hasTerrainObjs = null;
	public List<UnityEngine.GameObject> allterrainObjs = new List<UnityEngine.GameObject>();
	
	public static bool loadAllChunks = false;
	public Scene parentScene = null;
	
	public bool canUnloadChunk = true;
	
	ChunkPos lastChunkPos;
	
	public List<WorldSceneObject>[,,] worldObjs = null;
	
	private static float NEAR_SCENEOBJ_DIST = 30.0f;
	
	public WorldManager()
	{
		currinst = this;
		
		lastChunkPos.x = -1;
		lastChunkPos.y = -1;
	}

	public void createWorldObjs()
	{
		if(worldObjs != null)
			return;
		
		worldObjs = new List<WorldSceneObject>[chunkSplit, chunkSplit, 1];
		
		for(int i=0; i<chunkSplit; i++)
		{
			for(int ii=0; ii<chunkSplit; ii++)
			{
				worldObjs[i, ii, 0] = new List<WorldSceneObject>();
			}
		}
	}
	
	public void load()
	{
		terrainObjs = new UnityEngine.GameObject[chunkSplit, chunkSplit, 1];
		
		createWorldObjs();
		
		terrainAssetBundles = new Asset[chunkSplit, chunkSplit, 1];
		hasTerrainObjs = new bool[chunkSplit, chunkSplit, 1];
		
		if(load_splatPrototypes.Count > 0)
			splatPrototypes = new Texture2D[load_splatPrototypes.Count, 2];
		
		if(load_detailPrototypes.Count > 0)
			detailPrototypes = new Texture2D[load_detailPrototypes.Count];
		
		if(load_treePrototypes.Count > 0)
			treePrototypes = new UnityEngine.GameObject[load_treePrototypes.Count];
		
		HashSet<string> tmp = new HashSet<string>();
		for(int i=0; i<load_splatPrototypes.Count; i++)
		{
			tmp.Add(load_splatPrototypes[i].Key);
			if(load_splatPrototypes[i].Value != "")
				tmp.Add(load_splatPrototypes[i].Value);
		}
		
		for(int i=0; i<load_detailPrototypes.Count; i++)
		{
			tmp.Add(load_detailPrototypes[i]);
		}
		
		foreach(string s in tmp)
		{
			string ss = s + ".unity3d";
			
			Asset asset = new Asset();
			asset.type = Asset.TYPE.TERRAIN_DETAIL_TEXTURE;
			asset.loadPri = 1;
			asset.loadLevel = Asset.LOAD_LEVEL.LEVEL_ENTER_BEFORE;
			asset.source = ss;
			loader.inst.loadPool.addLoad(asset);
		}
		
		tmp.Clear();
		for(int i=0; i<load_treePrototypes.Count; i++)
		{
			tmp.Add(load_treePrototypes[i]);
		}
		
		foreach(string s in tmp)
		{
			string ss = s + ".unity3d";
			
			Asset asset = new Asset();
			asset.type = Asset.TYPE.TERRAIN_TREE;
			asset.loadPri = 1;
			asset.loadLevel = Asset.LOAD_LEVEL.LEVEL_ENTER_BEFORE;
			asset.source = ss;
			loader.inst.loadPool.addLoad(asset);
		}
		
		for(int i=0; i<chunkSplit; i++)
		{
			for(int ii=0; ii<chunkSplit; ii++)
			{
				hasTerrainObjs[i, ii, 0] = false;
			}
		}
		
		ChunkPos playerCurrChunkPos = atChunk();
		Common.DEBUG_MSG("WorldManager::load: player init at chunk(" + playerCurrChunkPos.x + "," + playerCurrChunkPos.y + ")");
		loadCurrentViewChunks();
		
		if(loadAllChunks == true)
		{
			for(int i=0; i<chunkSplit; i++)
			{
				for(int ii=0; ii<chunkSplit; ii++)
				{
					ChunkPos pos;
					pos.x = i;
					pos.y = ii;
					addLoadChunk(pos, 2, Asset.LOAD_LEVEL.LEVEL_ENTER_AFTER);
				}
			}
		}
	}
	
	public bool loadedWorldObjsCamera()
	{
		ChunkPos currChunk = atChunk();
		if(currChunk.x >= 0 && currChunk.y >= 0)
		{
			Vector3 currpos = RPG_Animation.instance.transform.position;
			currpos.y = 0.0f;
			
			foreach(WorldSceneObject obj in worldObjs[currChunk.x, currChunk.y, 0])
			{
				Vector3 pos = new Vector3(obj.position.x, 0.0f, obj.position.z);
				if(Vector3.Distance(pos, currpos) <= NEAR_SCENEOBJ_DIST)
				{
					if(obj.gameObject == null)
					{
						Common.DEBUG_MSG("WorldManager::loadedWorldObjsCamera: wait for " + obj.name + " load! " + "dist=" + Vector3.Distance(pos, currpos));
						return false;
					}
				}
			}
		}
		
		return true;
	}
	
	public void addLoadWorldObj(ChunkPos chunkIdx)
	{
		Vector3 currpos = RPG_Animation.instance.transform.position;
		currpos.y = 0.0f;
		
		worldObjs[chunkIdx.x, chunkIdx.y, 0].Sort(delegate(WorldSceneObject x, WorldSceneObject y) 
			{ 
				Vector3 pos1 = new Vector3(x.position.x, 0.0f, y.position.z);
				Vector3 pos2 = new Vector3(x.position.x, 0.0f, y.position.z);
				
				float dist1 = Vector3.Distance(pos1, currpos);
				float dist2 = Vector3.Distance(pos2, currpos);
				
				if(dist1 < dist2)
					return -1;

				if(dist1 > dist2)
					return 1;
				
				return 0;
			}
		);
		
		foreach(WorldSceneObject obj in worldObjs[chunkIdx.x, chunkIdx.y, 0])
		{
			Common.DEBUG_MSG("WorldManager::addLoadWorldObj:" + obj.asset.source +
				", isLoaded:" + obj.asset.isLoaded + ", loading:" + obj.asset.loading);
				
			if(obj.asset.isLoaded == false && obj.asset.loading == false)
			{
				SceneObject hasObj = null;
				if(parentScene.objs.TryGetValue(obj.idkey, out hasObj))
				{
					continue;
				}

				obj.asset.loadLevel = Asset.LOAD_LEVEL.LEVEL_ENTER_AFTER;
				
				parentScene.addSceneObject(obj.idkey, obj);
				obj.asset.refs.Add(obj.idkey);
				loader.inst.loadPool.addLoad(obj.asset);
			}
			else
			{
				if(obj.gameObject == null && obj.asset.isLoaded == true)
				{
					obj.Instantiate();
					obj.asset.refs.Remove(obj.idkey);
				}
			}
		}
	}
	
	public void addLoadChunk(ChunkPos chunkIdx, ushort loadPri, Asset.LOAD_LEVEL level)
	{
		if(chunkIdx.x < 0 || chunkIdx.y < 0 || chunkIdx.x >= chunkSplit || chunkIdx.y >= chunkSplit)
			return;
		
		Common.DEBUG_MSG("WorldManager::addLoadChunk: load(" + 
			(chunkIdx.x + 1) + "," + (chunkIdx.y + 1) + "), loaded=" + hasTerrainObjs[chunkIdx.x, chunkIdx.y, 0]);
		
		if(hasTerrainObjs[chunkIdx.x, chunkIdx.y, 0] == true)
			return;
		
		addLoadWorldObj(chunkIdx);
		Asset asset = new Asset();
		asset.type = Asset.TYPE.TERRAIN;
		asset.loadPri = loadPri;
		asset.loadLevel = level;
		asset.source = getChunkName(chunkIdx) + ".unity3d";
		loader.inst.loadPool.addLoad(asset);

		hasTerrainObjs[chunkIdx.x, chunkIdx.y, 0] = true;
	}
	
	public string getChunkName(ChunkPos chunkIdx)
	{
		return terrainName + "_" + (chunkIdx.y + 1) + "_" + (chunkIdx.x + 1);
	}
	
	public bool hasChunk(ChunkPos chunkIdx)
	{
		if(chunkIdx.x <= 0 || chunkIdx.y <= 0)
			return false;
		
		return hasTerrainObjs[chunkIdx.x, chunkIdx.y, 0];
	}
	
	public bool loadedChunk(ChunkPos chunkIdx)
	{
		if(chunkIdx.x <= 0 || chunkIdx.y <= 0)
			return false;
		
		UnityEngine.GameObject obj = terrainObjs[chunkIdx.x, chunkIdx.y, 0];
		if(obj != null)
		{
			return obj.GetComponent<Terrain>().enabled;
		}
		
		return false;
	}
	
	void clearSceneObjAtPoint(int x, int y, bool isAll)
	{
		foreach(WorldSceneObject obj1 in worldObjs[x, y, 0])
		{
			parentScene.objs.Remove(obj1.idkey);
			
			if(obj1.gameObject != null)
				UnityEngine.GameObject.Destroy(obj1.gameObject);
			
			obj1.gameObject = null;
			
			if(obj1.asset.refs.Count == 1 && obj1.asset.bundle != null)
			{
				Common.DEBUG_MSG("WorldManager::clearSceneObjAtPoint:" + obj1.asset.source);
				obj1.asset.isLoaded = false;
				obj1.asset.loading = false;
				obj1.asset.bundle.Unload(true);
				obj1.asset.bundle = null;
				obj1.asset.refs.Remove(obj1.idkey);
				if(obj1.asset.refs.Count != 0)
				{
					Common.ERROR_MSG("WorldManager::clearSceneObjAtPoint: asset.refs not found -> " + obj1.asset.source);
				}
			}
		}
		
		if(isAll == true)
			worldObjs[x, y, 0].Clear();
	}
	
	public void unload()
	{
		currinst = null;
		
		if(chunkSplit > 0)
		{
			name = "";
			terrainName = "";
			terrainObjs = null;
			
			for(int i=0; i<chunkSplit; i++)
			{
				for(int ii=0; ii<chunkSplit; ii++)
				{
					terrainAssetBundles[i, ii, 0].bundle.Unload(true);
					terrainAssetBundles[i, ii, 0] = null;
					clearSceneObjAtPoint(i, ii, true);
				}
			}

			loader.inst.loadPool.erase(Asset.TYPE.TERRAIN, false);
			loader.inst.loadPool.erase(Asset.TYPE.TERRAIN_DETAIL_TEXTURE, false);
			loader.inst.loadPool.erase(Asset.TYPE.TERRAIN_TREE, false);
			
			foreach(UnityEngine.GameObject obj in allterrainObjs)
			{
				UnityEngine.Object.Destroy(obj);
			}
			
			allterrainObjs.Clear();
			
			for(int i=0; i<load_splatPrototypes.Count; i++)
			{
				if(splatPrototypes[i, 0] != null)
					UnityEngine.Object.Destroy(splatPrototypes[i, 0]);
				
				if(splatPrototypes[i, 1] != null)
					UnityEngine.Object.Destroy(splatPrototypes[i, 1]);
			}
			
			for(int i=0; i<load_detailPrototypes.Count; i++)
			{
				if(detailPrototypes[i] != null)
					UnityEngine.Object.Destroy(detailPrototypes[i]);
			}
			
			for(int i=0; i<load_treePrototypes.Count; i++)
			{
				if(treePrototypes[i] != null)
					UnityEngine.Object.Destroy(treePrototypes[i]);
			}
			
			chunkSplit = 0;
		}
	}
	
	public void Update()
	{
		loadCurrentViewChunks();
	}
	
	public void loadCurrentViewChunks()
	{
		if(RPG_Animation.instance == null)
		{
			Common.DEBUG_MSG("WorldManager::loadCurrentViewChunks: RPG_Animation=" + RPG_Animation.instance);
			return;
		}
		
		ChunkPos currentPos = atChunk();
		
		if(lastChunkPos.x == currentPos.x && lastChunkPos.y == currentPos.y)
			return;
		
		Vector3 playerpos = RPG_Animation.instance.transform.position;
		playerpos.y = 0.0f;
		
		Common.DEBUG_MSG("WorldManager::loadCurrentViewChunks: pos(" + playerpos + "), changeChunk(" + 
			(lastChunkPos.x + 1) + "," + (lastChunkPos.y + 1) + ") ==> (" + 
			(currentPos.x + 1) + "," + (currentPos.y + 1) + ")");

		lastChunkPos = currentPos;
		
		// center
		addLoadChunk(currentPos, 0, Asset.LOAD_LEVEL.LEVEL_ENTER_AFTER);
		
		for(int i=0; i<chunkSplit; i++)
		{
			for(int ii=0; ii<chunkSplit; ii++)
			{
				int xdiff = Math.Abs(i - currentPos.x);
				int ydiff = Math.Abs(ii - currentPos.y);
				if(xdiff <= 1 && ydiff <= 1)
				{
					if(hasTerrainObjs[i, ii, 0] == true)
					{
						continue;
					}

					ChunkPos cpos;
					cpos.x = i;
					cpos.y = ii;
					addLoadChunk(cpos, 1, Asset.LOAD_LEVEL.LEVEL_ENTER_AFTER);
				}
				else
				{
					if(xdiff <= 2 && ydiff <= 2)
					{
						continue;
					}
					
					if(canUnloadChunk == false)
						continue;
					
					if(hasTerrainObjs[i, ii, 0] == true)
					{
						Common.DEBUG_MSG("WorldManager::loadCurrentViewChunks: unload(" + (i + 1) + "," + (ii + 1) + ")");
						hasTerrainObjs[i, ii, 0] = false;
						UnityEngine.GameObject obj = terrainObjs[i, ii, 0];
						if(obj != null)
						{
							allterrainObjs.Remove(obj);
							UnityEngine.GameObject.Destroy(obj);
						}
						
						terrainObjs[i, ii, 0] = null;
						
						Asset asset = terrainAssetBundles[i, ii, 0];
						if(asset != null)
						{
							asset.bundle.Unload(true);
							terrainAssetBundles[i, ii, 0] = null;
						}
						
						clearSceneObjAtPoint(i, ii, false);
					}
				}
			}
		}
		
		loader.inst.loadPool.start();
	}
	
	public static ChunkPos calcAtChunk(float x, float z, int chunkSplit, float chunkSize)
	{
		ChunkPos pos;
		pos.x = (int)(x / chunkSize);
		pos.y = (int)(z / chunkSize);
		
		if(pos.x >= chunkSplit)
			pos.x = -1;
		
		if(pos.y >= chunkSplit)
			pos.y = -1;

		return pos;
	}
	
	public ChunkPos atChunk()
	{
		if(RPG_Animation.instance == null)
		{
			ChunkPos pos;
			pos.x = 0;
			pos.y = 0;
			return pos;
		}
		
		Vector3 currpos = RPG_Animation.instance.transform.position;
		return calcAtChunk(currpos.x, currpos.z, chunkSplit, chunkSize);
	}
	
	void autoSetNeighbors(ChunkPos pos)
	{
		int arrayPos = 0;
		int terrainsLong = chunkSplit;
		int terrainsWide = chunkSplit;
		
		Terrain[] terrains = new Terrain[terrainsLong * terrainsWide];

		for(int y = 0; y < terrainsLong ; y++)
		{
			for(int x = 0; x < terrainsWide; x++)
			{
				if(terrainObjs[x, y, 0] != null)
					terrains[arrayPos] = terrainObjs[x, y, 0].GetComponent<Terrain>();
				else
					terrains[arrayPos] = null;
				
				arrayPos++;
			}
		}

		arrayPos = 0;
		for(int y = 0; y < terrainsLong ; y++)
		{
			for(int x = 0; x < terrainsWide; x++)
			{
				if(terrains[arrayPos] == null)
				{
					arrayPos++;
					continue;
				}

				if(y == 0)
				{
					if(x == 0)
						terrains[arrayPos].SetNeighbors(null, terrains[arrayPos + terrainsWide], terrains[arrayPos + 1], null);
					else if(x == terrainsWide - 1)
						terrains[arrayPos].SetNeighbors(terrains[arrayPos - 1], terrains[arrayPos + terrainsWide], null, null);
					else
						terrains[arrayPos].SetNeighbors(terrains[arrayPos - 1], terrains[arrayPos + terrainsWide], terrains[arrayPos + 1], null);
				}
				else if(y == terrainsLong - 1)
				{
					if(x == 0)
						terrains[arrayPos].SetNeighbors(null, null, terrains[arrayPos + 1], terrains[arrayPos - terrainsWide]);
					else if(x == terrainsWide - 1)
						terrains[arrayPos].SetNeighbors(terrains[arrayPos - 1], null, null, terrains[arrayPos - terrainsWide]);
					else
						terrains[arrayPos].SetNeighbors(terrains[arrayPos - 1], null, terrains[arrayPos + 1], terrains[arrayPos - terrainsWide]);
				}
				else
				{
					if(x == 0)
						terrains[arrayPos].SetNeighbors(null, terrains[arrayPos + terrainsWide], terrains[arrayPos + 1], terrains[arrayPos - terrainsWide]);
					else if(x == terrainsWide - 1)
						terrains[arrayPos].SetNeighbors(terrains[arrayPos - 1], terrains[arrayPos + terrainsWide], null, terrains[arrayPos - terrainsWide]);
					else
						terrains[arrayPos].SetNeighbors(terrains[arrayPos - 1], terrains[arrayPos + terrainsWide], terrains[arrayPos + 1], terrains[arrayPos - terrainsWide]);
				}
				
				arrayPos++;
			}	
		}
		
		/*
		for(int i = 0; i < terrainsWide*terrainsLong ; i++)
		{
			if(terrains[i] != null)
				terrains[i].Flush();
		}
		*/
	}
	
	public void onNewTerrain(ChunkPos currpos, UnityEngine.GameObject go)
	{
		Common.DEBUG_MSG("WorldManager::onNewTerrain: " + go.name + ", pos=" + 
			go.transform.position + ", dir=" + go.transform.rotation + ", scale=" + go.transform.localScale);
		
		autoSetNeighbors(currpos);
	}
	
	public void updateTerrainDetail(UnityEngine.GameObject go, bool refresh)
	{
		Terrain terrain = go.GetComponent<Terrain>();
		TerrainData terrainData = terrain.terrainData;
		bool changed = false;
		
		if(default_shader_diffuse != null)
			terrain.materialTemplate = new Material(default_shader_diffuse);
		
		SplatPrototype[] tsplatPrototypes = terrainData.splatPrototypes;
		
		for(int i=0; i<load_splatPrototypes.Count; i++)
		{
			if(splatPrototypes[i, 0] != null)
			{
				if(splatPrototypes[i, 0] != null && tsplatPrototypes[i].texture.name != splatPrototypes[i, 0].name)
				{
					tsplatPrototypes[i].texture = splatPrototypes[i, 0];
					changed = true;
					
					tsplatPrototypes[i].tileSize = splatPrototypes_titlesizeoffset[i].Key;
					tsplatPrototypes[i].tileOffset = splatPrototypes_titlesizeoffset[i].Value;
				}

				if(splatPrototypes[i, 1] != null && 
					(terrainData.splatPrototypes[i].normalMap == null || tsplatPrototypes[i].normalMap.name != splatPrototypes[i, 0].name))
				{
					tsplatPrototypes[i].texture = splatPrototypes[i, 1];
					changed = true;
				}
			}
		}
		
		if(changed == true)
			terrainData.splatPrototypes = tsplatPrototypes;
		
		DetailPrototype[] tDetailPrototypes = terrainData.detailPrototypes;
		bool detailChanged = false;
		for(int i=0; i<load_detailPrototypes.Count; i++)
		{
			if(detailPrototypes[i] != null)
			{
				if(detailPrototypes[i] != null && tDetailPrototypes[i].prototypeTexture.name != detailPrototypes[i].name)
				{
					tDetailPrototypes[i].prototypeTexture = detailPrototypes[i];
					changed = true;
					detailChanged = true;
				}
			}
		}
		
		if(detailChanged == true)
			terrainData.detailPrototypes = tDetailPrototypes;
		
		TreePrototype[] tTreePrototype = terrainData.treePrototypes;
		detailChanged = false;
		for(int i=0; i<load_treePrototypes.Count; i++)
		{
			if(treePrototypes[i] != null)
			{
				if(treePrototypes[i] != null && (tTreePrototype[i].prefab == null ||
					tTreePrototype[i].prefab.name != treePrototypes[i].name))
				{
					tTreePrototype[i].prefab = treePrototypes[i];
					changed = true;
					detailChanged = true;
				}
			}
		}
		
		if(detailChanged == true)
		{
			go.GetComponent<TerrainCollider>().enabled = false;
			terrainData.treePrototypes = tTreePrototype;
			go.GetComponent<TerrainCollider>().enabled = true;
		}
		
		if(refresh == true && changed == true)
		{
			terrainData.RefreshPrototypes();
			terrain.Flush();
		}
	}
	
	public override void onAssetAsyncLoadObjectCB(string name, UnityEngine.Object obj, Asset asset)
	{
		Common.DEBUG_MSG("WorldManager::onAssetAsyncLoadObjectCB: name(" + asset.mainAsset.name + ")!");
		
		if(asset.type == Asset.TYPE.TERRAIN)
		{
			UnityEngine.GameObject go = (UnityEngine.GameObject)obj; 
			go.name = asset.mainAsset.name;

			string[] names = asset.mainAsset.name.Split(new char[]{'_'});
			
			ChunkPos pos;
			pos.y = int.Parse(names[1]) - 1;
			pos.x = int.Parse(names[2]) - 1;
			
			terrainObjs[pos.x, pos.y, 0] = go;
			terrainAssetBundles[pos.x, pos.y, 0] = asset;
			hasTerrainObjs[pos.x, pos.y, 0] = true;
			
			allterrainObjs.Add(go);

			updateTerrainDetail(go, true);
			onNewTerrain(pos, go);
		}
		else if(asset.type == Asset.TYPE.TERRAIN_DETAIL_TEXTURE)
		{
			Texture2D go = (Texture2D)obj;
			go.name = asset.mainAsset.name;
			
			for(int i=0; i<load_splatPrototypes.Count; i++)
			{
				if(load_splatPrototypes[i].Key == go.name)
				{
					splatPrototypes[i, 0] = go;
				}
				
				if(load_splatPrototypes[i].Value == go.name)
				{
					splatPrototypes[i, 1] = go;
				}
			}
			
			for(int i=0; i<load_detailPrototypes.Count; i++)
			{
				if(load_detailPrototypes[i] == go.name)
				{
					detailPrototypes[i] = go;
				}
			}
			
			foreach(UnityEngine.GameObject obj1 in allterrainObjs)
			{
				updateTerrainDetail(obj1, true);
			}
		}
		else if(asset.type == Asset.TYPE.TERRAIN_TREE)
		{
			UnityEngine.GameObject go = (UnityEngine.GameObject)obj;
			Vector3 pos = go.transform.position;
			pos.y = -999999.0f;
			go.transform.position = pos;
			
			go.name = asset.mainAsset.name;
			for(int i=0; i<load_treePrototypes.Count; i++)
			{
				treePrototypes[i] = go;
			}
			
			foreach(UnityEngine.GameObject obj1 in allterrainObjs)
			{
				updateTerrainDetail(obj1, true);
			}
		}
		else if(asset.type == Asset.TYPE.WORLD_OBJ)
		{
		}

		// asset.bundle.Unload(true);
		// Scene.assetsCache.Add(asset.bundle.mainAsset.name, asset);
	}
	
	public void onGetDefaultTerrainShaser()
	{
		foreach(UnityEngine.GameObject obj in allterrainObjs)
		{
			Terrain terrain = obj.GetComponent<Terrain>();
			if(terrain.materialTemplate == null)
				terrain.materialTemplate = new Material(default_shader_diffuse);
		}
	}
}
