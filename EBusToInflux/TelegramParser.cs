using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EBusToInflux;

internal class TelegramParser
{
    const int HeaderBytesLength = 5;

    int headerToRead = 0;
    int bodyToRead = 0;
    private Telegram? curTelegram = null;
    bool crcRead = false;
    bool ackRead = false;
    int? responseBytesToRead = null;
    bool responseCRCRead = false;

    public Telegram? ContinueParse(byte b)
    {
        if (curTelegram == null && b != 0xAA)
        {
            // start something new!
            Reset();
            curTelegram = new Telegram();
            curTelegram.Sender = b;
            headerToRead = HeaderBytesLength - 1;
        }
        else if (curTelegram != null)
        {
            if (headerToRead == 4)
            {
                curTelegram.Receiver = b;
                headerToRead--;
            }
            else if (headerToRead == 3)
            {
                curTelegram.PrimaryCommand = b;
                headerToRead--;
            }
            else if (headerToRead == 2)
            {
                curTelegram.SecondaryCommand = b;
                headerToRead--;
            }
            else if (headerToRead == 1)
            {
                bodyToRead = b;
                curTelegram.Payload = new List<byte>(b);
                headerToRead--;
            }
            else if (bodyToRead > 0)
            {
                curTelegram.Payload.Add(b);
                bodyToRead--;
            }
            else if (bodyToRead == 0 && headerToRead == 0)
            {
                if (responseBytesToRead > 0)
                {
                    curTelegram.ResponsePayload.Add(b);
                    responseBytesToRead--;
                }
                else if (!crcRead)
                {
                    curTelegram.CRC = b;
                    crcRead = true;
                }
                else if (!ackRead)
                {
                    if (b == 0x00)
                        ackRead = true;
                    else if (b == 0xAA)
                    {
                        var result = curTelegram;
                        Reset();
                        return result;
                    }
                    else
                        Reset();
                }
                else if (crcRead && ackRead && responseBytesToRead == null)
                {
                    if(b != 0xAA)
                    {
                        responseBytesToRead = b;
                        curTelegram.ResponsePayload = new List<byte>(b);
                    }
                    else
                    {
                        var result = curTelegram;
                        Reset();
                        return result;
                    }
                }
                else if (!responseCRCRead && responseBytesToRead == 0)
                {
                    curTelegram.ResponseCRC = b;
                    responseCRCRead = true;
                }
                else if (crcRead && ackRead && responseCRCRead)
                {
                    if (b == 0x00) // last ack!
                    {
                        var result = curTelegram;
                        Reset();
                        return result;
                    }
                    else
                    {
                        Reset();
                    }
                }
            }

        }

        return null;
    }

    private void Reset()
    {
        headerToRead = 0;
        bodyToRead = 0;
        curTelegram = null;
        crcRead = false;
        ackRead = false;
        responseBytesToRead = null;
        responseCRCRead = false;
    }
}
