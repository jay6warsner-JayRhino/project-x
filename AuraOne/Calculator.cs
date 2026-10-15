using System.Globalization;
namespace AuraOne;
public static class Calculator
{
    public static string Evaluate(string expression){try{var p=new Parser(expression);var n=p.Sum();p.Space();return p.End&&double.IsFinite(n)?n.ToString("G12",CultureInfo.InvariantCulture):"Use numbers, parentheses and +, -, *, / or % only.";}catch{return "Use a numeric expression such as (12 + 8) / 2.";}}
    sealed class Parser(string text){int i;public bool End=>i>=text.Length;public void Space(){while(!End&&char.IsWhiteSpace(text[i]))i++;}bool Eat(char c){Space();if(!End&&text[i]==c){i++;return true;}return false;}public double Sum(){var n=Product();while(true){if(Eat('+'))n+=Product();else if(Eat('-'))n-=Product();else return n;}}double Product(){var n=Atom();while(true){if(Eat('*'))n*=Atom();else if(Eat('/'))n/=Atom();else if(Eat('%'))n%=Atom();else return n;}}double Atom(){Space();if(Eat('+'))return Atom();if(Eat('-'))return -Atom();if(Eat('(')){var n=Sum();if(!Eat(')'))throw new FormatException();return n;}var start=i;while(!End&&(char.IsAsciiDigit(text[i])||text[i]=='.'))i++;if(start==i)throw new FormatException();return double.Parse(text[start..i],CultureInfo.InvariantCulture);}}
}
