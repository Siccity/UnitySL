﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Xml;
using System.Xml.Serialization;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using HttpAPI;

public enum MaturityRating { PG = 1, Mature = 2, Adult = 4 }

public class UISearch : MonoBehaviour
{
    public enum Category { All, Events, Groups, People, Places, Wiki }

    public TMP_InputField searchInput;
    public UISearchItemTemplate items;
    public int start = 0;
    public Category category;
    public bool pg;
    public bool mature;
    public bool adult;
    [Header("Sprites")]
    public Sprite spritePlace;
    public Sprite spritePeople;
    public Sprite spriteEvents;
    public Sprite spriteGroups;
    public Sprite spriteRegion;
    public Sprite spriteAll;
    public Sprite spriteWiki;
    [Header("Results")]
    public ButtonTemplate resultButtons;
    [Header("Preview")]
    public TMP_Text title;
    public TMP_Text description;
    public RawImage map;
    public readonly List<Place> resultPlaces = new List<Place>();

    private void Start()
    {
        items.Initialize();
        if (resultButtons.template != null) resultButtons.Initialize();
    }

    public void Search()
    {
        items.Clear();

        MaturityRating rating = 0;
        if (pg) rating |= MaturityRating.PG;
        if (mature) rating |= MaturityRating.Mature;
        if (adult) rating |= MaturityRating.Adult;
        WWWFormPlus form = new WWWFormPlus();
        string url = $"http://search.secondlife.com/client_search.php?q={searchInput.text.ToLower()}&start={start}&mat={(int)rating}&output=xml_no_dtd&client=raw_xml_frontend&s={category.ToString()}";
        Debug.Log(url);
        form.Request(url, OnSearchFail, OnSearchSuccess);
    }

    public void OnSearchFail(string msg)
    {
        Debug.LogWarning("Fail: " + msg);
    }

    public void OnSearchSuccess(string text)
    {
        // Result comes with some symbols that cannot be parsed.
        // Manually pull these out for now till we figure what else to do.
        text = Regex.Replace(text, "&nbsp;", "");

        XmlDocument document = new XmlDocument();
        document.Load(new StringReader(text));

        items.Clear();
        resultPlaces.Clear();
        foreach (XmlNode node in document["html"]["body"]["div"].ChildNodes.Cast<XmlNode>().First(x => x.Attributes["class"].InnerText == "results_container"))
        {
            UISearchItem item = items.InstantiateTemplate();
            item.button.onClick.RemoveAllListeners();
            item.label.text = node["h3"].InnerText.Trim();

            switch (node.Attributes["class"].InnerText)
            {
                case "result place_icon":
                    {
                        item.icon.sprite = spritePlace;

                        Uri uri = new Uri(node["h3"]["a"].Attributes["href"].InnerText);
                        Guid.TryParse(uri.Segments.Last(), out Guid guid);
                        string name = node["h3"].InnerText.Trim();
                        string desc = node["p"].InnerText.Trim();

                        //Place place = new Place(guid.ToString(), name, desc);
                        //resultPlaces.Add(place);
                        //item.button.onClick.AddListener(() => PreviewPlace(place));

                        item.button.onClick.AddListener(() => Debug.Log($"{name} ({guid})\n{desc}"));
                        break;
                    }
                case "result resident_icon":
                    {
                        item.icon.sprite = spritePeople;
                        string name = node["h3"].InnerText.Trim();
                        string displayName;
                        string userName;
                        Match match = Regex.Match(name, "(.*) \\((.+)\\)$");
                        if (match.Success)
                        {
                            displayName = match.Groups[1].Value;
                            userName = match.Groups[2].Value;
                        }
                        else
                        {
                            displayName = name;
                            userName = name;
                        }
                        string desc = node["p"].InnerText.Trim();
                        item.button.onClick.AddListener(() => Debug.Log($"{displayName} ({userName})\n{desc}"));
                        break;
                    }
                case "result group_icon":
                    {
                        item.icon.sprite = spriteGroups;
                        Uri uri = new Uri(node["h3"]["a"].Attributes["href"].InnerText);
                        Guid.TryParse(uri.Segments.Last(), out Guid guid);
                        string name = node["h3"].InnerText.Trim();
                        string desc = node["p"].InnerText.Trim();

                        item.button.onClick.AddListener(() => Debug.Log($"{name} ({guid})\n{desc}"));
                        break;
                    }
                case "result region_icon":
                    {
                        item.icon.sprite = spriteRegion;
                        Uri uri = new Uri(node["h3"]["a"].Attributes["href"].InnerText);
                        Guid.TryParse(uri.Segments.Last(), out Guid guid);
                        string name = node["h3"].InnerText.Trim();
                        string desc = node["p"].InnerText.Trim();

                        item.button.onClick.AddListener(() => Debug.Log($"{name} ({guid})\n{desc}"));
                        break;
                    }
                case "result event_icon":
                    {
                        item.icon.sprite = spriteEvents;
                        Uri uri = new Uri(node["h3"]["a"].Attributes["href"].InnerText);
                        string id = uri.Segments.Last();
                        string name = node["h3"].InnerText.Trim();
                        string desc = node["p"].InnerText.Trim();

                        item.button.onClick.AddListener(() => Debug.Log($"{name} ({id})\n{desc}"));
                        break;
                    }
                default:
                    {
                        item.icon.sprite = spriteAll;
                        Debug.LogWarning("Search result of type '" + node.Attributes["class"].InnerText + "' not supported yet.");
                        break;
                    }
            }
        }
    }

    public void PreviewPlace(Place place)
    {
        place.FetchDetails(Debug.Log, PreviewPlaceDetailed);
    }

    private void PreviewPlaceDetailed(Place place)
    {
        // Title
        title.text = place.title;

        // Description
        description.text = place.description;

        // Image
        HttpAPI.Region region = new HttpAPI.Region(place.region);
        region.GetMap(Debug.LogWarning, x => map.texture = x);

        // Buttons
        resultButtons.Clear();
        Button linkButton = resultButtons.InstantiateTemplate();
        linkButton.onClick.AddListener(() => Application.OpenURL($"https://world.secondlife.com/place/{place.guid}"));
        linkButton.GetComponentInChildren<TMP_Text>().text = "Link to page";
        Button findButton = resultButtons.InstantiateTemplate();
        findButton.onClick.AddListener(() => Application.OpenURL($"https://maps.secondlife.com/secondlife/{place.region}/{place.location.x}/{place.location.y}/{place.location.z}/"));
        findButton.GetComponentInChildren<TMP_Text>().text = "Find on map";
        Button tpButton = resultButtons.InstantiateTemplate();
        tpButton.onClick.AddListener(() => Application.OpenURL($"secondlife:///app/teleport/{place.region}/{place.location.x}/{place.location.y}/{place.location.z}/"));
        tpButton.GetComponentInChildren<TMP_Text>().text = "Teleport";
    }

    public void SetCategory(Category category)
    {
        this.category = category;
        Search();
    }

    [Serializable] public class UISearchItemTemplate : Template<UISearchItem> { };
    [Serializable] public class ButtonTemplate : Template<Button> { };
}