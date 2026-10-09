using AiUstozPro.Application.Import;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;

namespace AiUstozPro.Tests;

public class ImportTests
{
    [Fact]
    public void Csv_nuqtali_vergul_va_qoshtirnoq_bilan()
    {
        var d = CsvReader.Parse("﻿T/r;Mavzu;Soat\n1;\"Algoritm; tushuncha\";2\n2;\"Uning \"\"turlari\"\"\";2\n");
        Assert.Equal(new[] { "T/r", "Mavzu", "Soat" }, d.Headers.ToArray());
        Assert.Equal(2, d.Rows.Count);
        Assert.Equal("Algoritm; tushuncha", d.Rows[0][1]);
        Assert.Equal("Uning \"turlari\"", d.Rows[1][1]);
    }

    [Fact]
    public void Csv_vergul_ajratgich_avtomatik_aniqlanadi()
    {
        var d = CsvReader.Parse("Familiya,Ism\r\nAliyev,Test\r\n\r\n");
        Assert.Equal(2, d.Headers.Count);
        Assert.Single(d.Rows);
    }

    [Fact]
    public void Ustunlar_ozbekcha_sarlavhalar_boyicha_moslanadi()
    {
        var map = ColumnMapper.AutoMap(new[] { "№", "Mavzu nomi", "Ajratilgan soat", "Mashg‘ulot turi", "Izoh" }, TopicImport.Fields);
        Assert.Equal(0, map["order"]);
        Assert.Equal(1, map["title"]);
        Assert.Equal(2, map["hours"]);
        Assert.Equal(3, map["type"]);
        Assert.Equal(4, map["note"]);
    }

    [Fact]
    public void Majburiy_ustun_bolmasa_xato()
    {
        var d = CsvReader.Parse("Mavzu;Izoh\nA;b\n");
        var map = ColumnMapper.AutoMap(d.Headers, TopicImport.Fields);
        var res = TopicImport.Validate(d, map);
        var r = Assert.Single(res);
        Assert.False(r.IsValid);
        Assert.Contains("Soat", r.Errors[0]);
    }

    [Fact]
    public void Notogri_satrlar_aniqlanadi_togrilari_qabul_qilinadi()
    {
        var d = CsvReader.Parse("T/r;Mavzu;Soat;Turi\n1;A;2;Nazariy\n2;;2;Amaliy\n3;C;ikki;Amaliy\n4;D;0;\n5;E;2,0;Laboratoriya\n6;F;2;nimadir\n");
        var res = TopicImport.Validate(d, ColumnMapper.AutoMap(d.Headers, TopicImport.Fields));
        Assert.Equal(6, res.Count);
        Assert.True(res[0].IsValid);
        Assert.False(res[1].IsValid); // bo'sh mavzu
        Assert.False(res[2].IsValid); // "ikki"
        Assert.False(res[3].IsValid); // 0 soat
        Assert.True(res[4].IsValid);
        Assert.Equal(TopicType.Laboratory, res[4].Item!.Type);
        Assert.True(res[5].IsValid);
        Assert.NotEmpty(res[5].Warnings); // tur tanilmadi
        Assert.Equal(3, res[1].RowNumber);
        Assert.Equal(TopicType.Practice, TopicImport.ParseType("amaliy", out _));
        Assert.Equal(TopicType.Control, TopicImport.ParseType("Nazorat ishi", out _));
    }

    [Fact]
    public void Oquvchi_raqami_takrorlanishi_xato_bir_xil_ism_faqat_ogohlantirish()
    {
        var d = CsvReader.Parse("Familiya;Ism;Raqami\nAliyev;Vali;1\nAliyev;Vali;2\nKarimov;Ali;1\nSodiqov;Bek;99\n");
        var map = ColumnMapper.AutoMap(d.Headers, StudentImport.Fields);
        var res = StudentImport.Validate(d, map, new HashSet<string> { "99" }, new HashSet<string>());
        Assert.True(res[0].IsValid);
        Assert.True(res[1].IsValid);
        Assert.NotEmpty(res[1].Warnings);   // bir xil F.I.Sh. — birlashtirilmaydi
        Assert.False(res[2].IsValid);       // raqam faylda takrorlangan
        Assert.False(res[3].IsValid);       // raqam tizimda bor
    }
}

public class SecurityTests
{
    [Fact]
    public void Parol_xeshlanadi_va_tekshiriladi()
    {
        var h = PasswordHasher.Hash("Parol2026");
        Assert.DoesNotContain("Parol2026", h);
        Assert.True(PasswordHasher.Verify("Parol2026", h));
        Assert.False(PasswordHasher.Verify("parol2026", h));
        Assert.NotEqual(h, PasswordHasher.Hash("Parol2026")); // tuz tasodifiy
        Assert.False(PasswordHasher.Verify("x", "buzilgan$format"));
    }

    [Fact]
    public void Parol_talablari()
    {
        Assert.NotNull(PasswordHasher.ValidateStrength("qisqa1"));
        Assert.NotNull(PasswordHasher.ValidateStrength("faqatharflar"));
        Assert.Null(PasswordHasher.ValidateStrength("yaxshiParol1"));
    }

    [Theory]
    [InlineData(UserRole.Administrator, Permission.ManageUsers, true)]
    [InlineData(UserRole.Teacher, Permission.ManageUsers, false)]
    [InlineData(UserRole.Teacher, Permission.TakeAttendance, true)]
    [InlineData(UserRole.Observer, Permission.TakeAttendance, false)]
    [InlineData(UserRole.Observer, Permission.ViewReports, true)]
    [InlineData(UserRole.Observer, Permission.EditCurriculum, false)]
    public void Rollar_ruxsatlari(UserRole role, Permission p, bool expected)
        => Assert.Equal(expected, Permissions.Has(role, p));
}
