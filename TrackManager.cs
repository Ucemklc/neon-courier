using System.Collections.Generic;
using UnityEngine;

public class TrackManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Transform player;

    [SerializeField]
    private GameObject trackTilePrefab;

    [Header("Track Settings")]
    [SerializeField, Min(3)]
    private int tileCount = 7;

    [SerializeField, Min(5f)]
    private float tileLength = 30f;

    [SerializeField]
    private float initialStartZ = -10f;

    [Header("Recycling")]
    [SerializeField, Min(0f)]
    private float recycleBehindDistance = 8f;

    private readonly List<Transform>
        activeTiles =
            new List<Transform>();

    private float nextTileZ;

    private void Start()
    {
        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        CreateInitialTrack();
    }

    private void Update()
    {
        RecyclePassedTiles();
    }

    private bool ValidateReferences()
    {
        if (player == null)
        {
            Debug.LogError(
                "TrackManager: Player referansı eksik.",
                this
            );

            return false;
        }

        if (trackTilePrefab == null)
        {
            Debug.LogError(
                "TrackManager: Track Tile Prefab eksik.",
                this
            );

            return false;
        }

        return true;
    }

    private void CreateInitialTrack()
    {
        activeTiles.Clear();

        for (
            int index = 0;
            index < tileCount;
            index++
        )
        {
            float spawnZ =
                initialStartZ +
                (index * tileLength);

            CreateTile(
                spawnZ,
                index == 0
            );
        }

        nextTileZ =
            initialStartZ +
            (tileCount * tileLength);
    }

    private void CreateTile(
        float spawnZ,
        bool shouldBeClear
    )
    {
        GameObject tileInstance =
            Instantiate(
                trackTilePrefab,
                new Vector3(
                    0f,
                    0f,
                    spawnZ
                ),
                Quaternion.identity,
                transform
            );

        tileInstance.name =
            $"TrackTile_{activeTiles.Count:00}";

        RefreshTileSystems(
            tileInstance,
            shouldBeClear
        );

        activeTiles.Add(
            tileInstance.transform
        );
    }

    private void RecyclePassedTiles()
    {
        for (
            int index = 0;
            index < activeTiles.Count;
            index++
        )
        {
            Transform tile =
                activeTiles[index];

            float tileEndZ =
                tile.position.z +
                tileLength;

            bool hasPassedTile =
                player.position.z >
                tileEndZ +
                recycleBehindDistance;

            if (!hasPassedTile)
            {
                continue;
            }

            tile.position =
                new Vector3(
                    0f,
                    0f,
                    nextTileZ
                );

            RefreshTileSystems(
                tile.gameObject,
                false
            );

            nextTileZ +=
                tileLength;
        }
    }

    private void RefreshTileSystems(
        GameObject tileObject,
        bool shouldBeClear
    )
    {
        if (tileObject == null)
        {
            return;
        }

        TrackTileContent tileContent =
            tileObject.GetComponent
                <TrackTileContent>();

        if (tileContent != null)
        {
            if (shouldBeClear)
            {
                tileContent.ClearPattern();
            }
            else
            {
                tileContent.RefreshPattern();
            }
        }

        CityTileDecorator cityDecorator =
            tileObject.GetComponent
                <CityTileDecorator>();

        if (cityDecorator != null)
        {
            cityDecorator.RefreshLayout();
        }
        var district = tileObject.GetComponent<CourierDistrictTile>() ?? tileObject.AddComponent<CourierDistrictTile>();
        district.Refresh();
    }
}
