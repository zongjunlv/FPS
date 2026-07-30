using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class RuntimeGameObjectPool
{
    private readonly GameObject[] instances;
    private readonly bool[] rented;
    private readonly int[] availableIndices;
    private readonly Dictionary<GameObject, int> indicesByInstance;
    private int availableCount;

    public int Capacity => instances.Length;

    public int AvailableCount => availableCount;

    public int ActiveCount => Capacity - availableCount;

    public RuntimeGameObjectPool(
        int capacity,
        Func<int, GameObject> factory)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "Pool capacity must be greater than zero.");
        }

        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        instances = new GameObject[capacity];
        rented = new bool[capacity];
        availableIndices = new int[capacity];
        indicesByInstance =
            new Dictionary<GameObject, int>(capacity);

        for (int index = 0; index < capacity; index++)
        {
            GameObject instance = factory(index);

            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"Pool factory returned null at index {index}.");
            }

            if (indicesByInstance.ContainsKey(instance))
            {
                throw new InvalidOperationException(
                    "Pool factory must return a unique object " +
                    $"for every index; duplicate at index {index}.");
            }

            instance.SetActive(false);
            instances[index] = instance;
            indicesByInstance.Add(instance, index);
            availableIndices[availableCount] = index;
            availableCount++;
        }
    }

    public GameObject Rent()
    {
        if (availableCount == 0)
        {
            return null;
        }

        int index = availableIndices[--availableCount];
        GameObject instance = instances[index];
        rented[index] = true;
        instance.SetActive(true);
        return instance;
    }

    public bool Return(GameObject instance)
    {
        if (instance == null ||
            !indicesByInstance.TryGetValue(instance, out int index) ||
            !rented[index])
        {
            return false;
        }

        rented[index] = false;
        instance.SetActive(false);
        availableIndices[availableCount++] = index;
        return true;
    }

    public void ReturnAll()
    {
        for (int index = 0; index < instances.Length; index++)
        {
            if (!rented[index])
            {
                continue;
            }

            rented[index] = false;
            instances[index].SetActive(false);
            availableIndices[availableCount++] = index;
        }
    }
}
