package com.motalim.adawat.docs

import org.xmlpull.v1.XmlPullParser
import org.xmlpull.v1.XmlPullParserFactory
import java.io.InputStream

/** A small XML tree, enough to read Office and EPUB parts. Names keep their prefixes ("w:p"). */
class XNode(val name: String, val attrs: Map<String, String>, val parent: XNode?) {
    val children = mutableListOf<XNode>()
    var text: String = ""

    val isText get() = name == "#text"

    fun attr(name: String) = attrs[name]

    fun child(name: String) = children.firstOrNull { it.name == name }

    fun elements(name: String) = children.filter { it.name == name }

    fun descendants(): Sequence<XNode> = sequence { for (c in children) { yield(c); yieldAll(c.descendants()) } }

    fun descendants(name: String) = descendants().filter { it.name == name }

    /** The nearest ancestor of that name. */
    fun ancestor(name: String): XNode? { var p = parent; while (p != null && p.name != name) p = p.parent; return p }

    /** All the text inside, as written. */
    val innerText: String get() = if (isText) text else children.joinToString("") { it.innerText }

    val localName get() = name.substringAfter(':')
}

object Xml {
    fun parse(input: InputStream): XNode {
        val parser = XmlPullParserFactory.newInstance().apply { isNamespaceAware = false }.newPullParser()
        parser.setInput(input, null)
        val root = XNode("#document", emptyMap(), null)
        var current = root
        while (true) {
            when (parser.next()) {
                XmlPullParser.START_TAG -> {
                    val attrs = (0 until parser.attributeCount).associate { parser.getAttributeName(it) to parser.getAttributeValue(it) }
                    val node = XNode(parser.name, attrs, current)
                    current.children += node
                    current = node
                }
                XmlPullParser.END_TAG -> current = current.parent ?: root
                XmlPullParser.TEXT -> current.children += XNode("#text", emptyMap(), current).apply { text = parser.text }
                XmlPullParser.END_DOCUMENT -> return root
            }
        }
    }

    fun escape(text: String) = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("\"", "&quot;")
}
