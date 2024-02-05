目前支援：
>* 盤中成交重心的報價與2個自訂指標
>* 盤中漲幅排行中，站上月線的標的
>* 歷史損益
>* MXF下單與回報
btw: ipynb is a prototype, the executable code would be compacked as a method in Program.cs  

```C#
SJ InitSJ = new();
InitSJ.Initialize("your api key file path");
InitSJ.AmountRankSetQuoteCallback(15);
```

![simulation](https://i.imgur.com/RO3PpRc.png)


```C#
InitSJ.Pnl("2024-01-10", "2024-01-10");
```
![simulation](https://i.imgur.com/ASqckqe.png)
